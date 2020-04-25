#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
#endif
using UnityEngine;

namespace UnityHierarchyFolders.Runtime {
#if UNITY_EDITOR
    /// <summary>
    /// <para>Extension to Components to check if there are no dependencies to itself.</para>
    /// <para>
    ///     taken from:
    ///     <see cref="!:https://gamedev.stackexchange.com/a/140799">
    ///         StackOverflow: Check if a game object's component can be destroyed
    ///     </see>
    /// </para>
    /// </summary>
    static class CanDestroyExtension
    {
        private static bool Requires(Type obj, Type req) => Attribute.IsDefined(obj, typeof(RequireComponent)) &&
            Attribute.GetCustomAttributes(obj, typeof(RequireComponent))
                .OfType<RequireComponent>()
                // RequireComponent has up to 3 required types per requireComponent, because of course.
                .SelectMany(rc => new Type[] { rc.m_Type0, rc.m_Type1, rc.m_Type2 })
                .Any(t => t != null && t.IsAssignableFrom(req));

        /// <summary>Checks whether the stated component can be destroyed without violating dependencies.</summary>
        /// <returns>Is component destroyable?</returns>
        /// <param name="t">Component candidate for destruction.</param>
        internal static bool CanDestroy(this Component t) => !t.gameObject.GetComponents<Component>()
            .Any(c => Requires(c.GetType(), t.GetType()));
    }
#endif

    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class Folder : MonoBehaviour
    {
#if UNITY_EDITOR
        private static bool addedSelectionResetCallback;

        private Folder()
        {
            // add reset callback first in queue
            if (!addedSelectionResetCallback)
            {
                Selection.selectionChanged += () => Tools.hidden = false;
                addedSelectionResetCallback = true;
            }

            Selection.selectionChanged += this.HandleSelection;
        }

        private static Tool lastTool;
        private static Folder toolLock;

        [SerializeField]
        private int _colorIndex;

        public int colorIndex => _colorIndex;
        
        /// <summary>
        /// The set of folder objects.
        /// </summary>
        public static Dictionary<int, int> folders = new Dictionary<int, int>();

        /// <summary>
        /// Gets the icon index associated with the specified object.
        /// </summary>
        /// <param name="obj">Test object.</param>
        /// <param name="index">The icon index.</param>
        /// <returns>True if the specified object is a folder with a registered icon index.</returns>
        public static bool TryGetIconIndex(UnityEngine.Object obj, out int index)
        {
            index = -1;
            return obj && folders.TryGetValue(obj.GetInstanceID(), out index);
        }

        /// <summary>
        /// Test if a Unity object is a folder by way of containing a Folder component.
        /// </summary>
        /// <param name="obj">Test object.</param>
        /// <returns>Is this object a folder?</returns>
        public static bool IsFolder(UnityEngine.Object obj) => folders.ContainsKey(obj.GetInstanceID());
 
        private void Start() => AddOrUpdateFolderData();
        private void OnValidate() =>  AddOrUpdateFolderData();
        private void OnDestroy() => RemoveFolderData();
        
        private void RemoveFolderData()
        {
            var instanceId = gameObject.GetInstanceID();
            if (folders.ContainsKey(instanceId))
            {
                folders.Remove(gameObject.GetInstanceID());
            }
        }

        private void AddOrUpdateFolderData()
        {
            var instanceId = gameObject.GetInstanceID();
            if (folders.ContainsKey(instanceId))
            {
                folders[instanceId] = _colorIndex;
            }
            else
            {
                folders.Add(instanceId, _colorIndex);
            }
        }

        /// <summary>Hides all gizmos if selected to avoid accidental editing of the transform.</summary>
        private void HandleSelection()
        {
            // ignore if another folder object is already hiding gizmo
            if (toolLock != null && toolLock != this) { return; }

            if (this != null && Selection.Contains(this.gameObject))
            {
                lastTool = Tools.current;
                toolLock = this;
                Tools.current = Tool.None;
            }
            else if (toolLock != null)
            {
                Tools.current = lastTool;
                toolLock = null;
            }
        }

        private bool AskDelete() => EditorUtility.DisplayDialog(
            title: "Can't add script",
            message: "Folders shouldn't be used with other components. Which component should be kept?",
            ok: "Folder",
            cancel: "Component"
        );

        /// <summary>Delete all components regardless of dependency hierarchy.</summary>
        /// <param name="comps">Which components to delete.</param>
        private void DeleteComponents(IEnumerable<Component> comps)
        {
            var destroyable = comps.Where(c => c != null && c.CanDestroy());

            // keep cycling through the list of components until all components are gone.
            while (destroyable.Any())
            {
                foreach (var c in destroyable)
                {
                    DestroyImmediate(c);
                }
            }
        }

        /// <summary>Ensure that the folder is the only component.</summary>
        private void EnsureExclusiveComponent()
        {
            // we are running, don't bother the player.
            // also, sometimes `this` might be null for whatever reason.
            if (Application.isPlaying || this == null)
            {
                return;
            }

            var existingComponents = this.GetComponents<Component>()
                .Where(c => c != this && !typeof(Transform).IsAssignableFrom(c.GetType()));

            // no items means no actions anyways
            if (!existingComponents.Any()) {  return; }

            if (this.AskDelete())
            {
                this.DeleteComponents(existingComponents);
            }
            else
            {
                DestroyImmediate(this);
            }
        }

        /// <summary>
        /// Hide inspector to prevent accidental editing of transform.
        /// </summary>
        private void OnEnable() => this.transform.hideFlags = HideFlags.HideInInspector;
#endif

        /// <summary>
        /// Resets the transform properties to their identities, i.e. (0, 0, 0), (0˚, 0˚, 0˚), and (100%, 100%, 100%).
        /// </summary>
        private void Update()
        {
            this.transform.position = Vector3.zero;
            this.transform.rotation = Quaternion.identity;
            this.transform.localScale = new Vector3(1, 1, 1);

#if UNITY_EDITOR
            if (!Application.IsPlaying(gameObject))
            {
                AddOrUpdateFolderData();
            }
            
            this.EnsureExclusiveComponent();
#endif
        }

        /// <summary>Takes direct children and links them to the parent transform or global.</summary>
        public void Flatten()
        {
            // gather first-level children
            foreach (Transform child in this.transform.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                var index = transform.GetSiblingIndex();
                if (child.parent == this.transform)
                {
                    child.name = $"{this.name}/{child.name}";
                    child.parent = this.transform.parent;
                    child.SetSiblingIndex(++index);
                }
            }

            if (Application.isPlaying)
            {
                Destroy(this.gameObject);
            }
            else
            {
                DestroyImmediate(this.gameObject);
            }
        }
    }
}
