#if !UNITY_2019_1_OR_NEWER
#define CINEMACHINE_TIMELINE
#endif
#if CINEMACHINE_TIMELINE

using UnityEditor;
using UnityEngine;
using Cinemachine.Editor;
using System.Collections.Generic;
using UnityEditor.Timeline;
using Cinemachine;

//namespace Cinemachine.Timeline
//{
    [CustomEditor(typeof(CinemachineShot))]
    internal sealed class CinemachineShotEditor : BaseEditor<CinemachineShot>
    {
        static string kAutoCreateKey = "CM_Timeline_AutoCreateShotFromSceneView";
        public static bool AutoCreateShotFromSceneView
        {
            get { return EditorPrefs.GetBool(kAutoCreateKey, false); }
            set
            {
                if (value != AutoCreateShotFromSceneView)
                    EditorPrefs.SetBool(kAutoCreateKey, value);
            }
        }

        static string kUseScrubbingCache = "CNMCN_Timeeline_UseTimelineScrubbingCache";
        public static bool UseScrubbingCache
        {
            get { return EditorPrefs.GetBool(kUseScrubbingCache, true); }
            set
            {
                if (UseScrubbingCache != value)
                {
                    EditorPrefs.SetBool(kUseScrubbingCache, value);
                    TargetPositionCache.UseCache = value;
                }
            }
        }

        [InitializeOnLoad]
        public class SyncCacheEnabledSetting
        {
            static SyncCacheEnabledSetting()
            {
                TargetPositionCache.UseCache = UseScrubbingCache;
            }
        }

        static public CinemachineVirtualCameraBase CreateStaticVcamFromSceneView()
        {
            CinemachineVirtualCameraBase vcam = CinemachineMenu.CreateStaticVirtualCamera();
            vcam.m_StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;

#if false 
            // GML this is too bold.  What if timeline is a child of something moving?
            // also, SetActive(false) prevents the animator from being able to animate the object
            vcam.gameObject.SetActive(false);
    #if UNITY_2018_3_OR_NEWER
            var d = TimelineEditor.inspectedDirector;
            if (d != null)
                Undo.SetTransformParent(vcam.transform, d.transform, "");
    #endif
#endif
            return vcam;
        }

        private static readonly GUIContent kVirtualCameraLabel
            = new GUIContent("Virtual Camera", "The virtual camera to use for this shot");

        protected override void GetExcludedPropertiesInInspector(List<string> excluded)
        {
            base.GetExcludedPropertiesInInspector(excluded);
            excluded.Add(FieldPath(x => x.VirtualCamera));
        }

        private void OnDisable()
        {
            DestroyComponentEditors();
        }

        private void OnDestroy()
        {
            DestroyComponentEditors();
        }

        public override void OnInspectorGUI()
        {
            BeginInspector();
            SerializedProperty vcamProperty = FindProperty(x => x.VirtualCamera);
            EditorGUI.indentLevel = 0; // otherwise subeditor layouts get screwed up

            AutoCreateShotFromSceneView
                = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Auto-create new shots",  "When enabled, new clips will be "
                            + "automatically populated to match the scene view camera.  "
                            + "This is a global setting"),
                    AutoCreateShotFromSceneView);

            UseScrubbingCache
                = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Use Scrubbing Cache",
                        "For preview playback, use a cache to approximate damping "
                            + "and noise playback.  This is a global setting."),
                    UseScrubbingCache);
    
            Rect rect;
            CinemachineVirtualCameraBase vcam
                = vcamProperty.exposedReferenceValue as CinemachineVirtualCameraBase;
            if (vcam != null)
                EditorGUILayout.PropertyField(vcamProperty, kVirtualCameraLabel);
            else
            {
                GUIContent createLabel = new GUIContent("Create");
                Vector2 createSize = GUI.skin.button.CalcSize(createLabel);

                rect = EditorGUILayout.GetControlRect(true);
                rect.width -= createSize.x;

                EditorGUI.PropertyField(rect, vcamProperty, kVirtualCameraLabel);
                rect.x += rect.width; rect.width = createSize.x;
                if (GUI.Button(rect, createLabel))
                {
                    vcam = CreateStaticVcamFromSceneView();
                    vcamProperty.exposedReferenceValue = vcam;
                }
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.BeginChangeCheck();
            DrawRemainingPropertiesInInspector();

            if (vcam != null)
                DrawSubeditors(vcam);

            // by default timeline rebuilds the entire graph when something changes,
            // but if a property of the virtual camera changes, we only need to re-evaluate the timeline.
            // this prevents flicker on post processing updates
            if (EditorGUI.EndChangeCheck())
            {
#if UNITY_2018_3_OR_NEWER
                TimelineEditor.Refresh(RefreshReason.SceneNeedsUpdate);
#endif
                GUI.changed = false;
            }
        }

        void DrawSubeditors(CinemachineVirtualCameraBase vcam)
        {
            // Create an editor for each of the cinemachine virtual cam and its components
            GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            UpdateComponentEditors(vcam);
            if (m_editors != null)
            {
                foreach (UnityEditor.Editor e in m_editors)
                {
                    if (e == null || e.target == null || (e.target.hideFlags & HideFlags.HideInInspector) != 0)
                        continue;

                    // Separator line - how do you make a thinner one?
                    GUILayout.Box("", new GUILayoutOption[] { GUILayout.ExpandWidth(true), GUILayout.Height(1) } );

                    bool expanded = true;
                    if (!s_EditorExpanded.TryGetValue(e.target.GetType(), out expanded))
                        expanded = true;
                    expanded = EditorGUILayout.Foldout(
                        expanded, e.target.GetType().Name, true, foldoutStyle);
                    if (expanded)
                        e.OnInspectorGUI();
                    s_EditorExpanded[e.target.GetType()] = expanded;
                }
            }
        }

        CinemachineVirtualCameraBase m_cachedReferenceObject;
        UnityEditor.Editor[] m_editors = null;
        static Dictionary<System.Type, bool> s_EditorExpanded = new Dictionary<System.Type, bool>();

        void UpdateComponentEditors(CinemachineVirtualCameraBase obj)
        {
            MonoBehaviour[] components = null;
            if (obj != null)
                components = obj.gameObject.GetComponents<MonoBehaviour>();
            int numComponents = (components == null) ? 0 : components.Length;
            int numEditors = (m_editors == null) ? 0 : m_editors.Length;
            if (m_cachedReferenceObject != obj || (numComponents + 1) != numEditors)
            {
                DestroyComponentEditors();
                m_cachedReferenceObject = obj;
                if (obj != null)
                {
                    m_editors = new UnityEditor.Editor[components.Length + 1];
                    CreateCachedEditor(obj.gameObject.GetComponent<Transform>(), null, ref m_editors[0]);
                    for (int i = 0; i < components.Length; ++i)
                        CreateCachedEditor(components[i], null, ref m_editors[i + 1]);
                }
            }
        }

        void DestroyComponentEditors()
        {
            m_cachedReferenceObject = null;
            if (m_editors != null)
            {
                for (int i = 0; i < m_editors.Length; ++i)
                {
                    if (m_editors[i] != null)
                        UnityEngine.Object.DestroyImmediate(m_editors[i]);
                    m_editors[i] = null;
                }
                m_editors = null;
            }
        }
    }
//}
#endif
