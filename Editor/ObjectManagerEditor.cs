using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System.Linq;

namespace JulienNoe.Tools.ObjectManager
{
    // This attribute ensures the class initializes when Unity loads
    [InitializeOnLoad]
    public class ObjectManagerEditor : EditorWindow
    {
        // Toggle to show or hide the help box
        private bool showHelp = false;

        // List of recently selected objects tracked by the tool
        private List<Object> recentObjects = new List<Object>();

        // List of favorite objects manually added by the user
        private List<Object> favoriteObjects = new List<Object>();

        // Maximum number of recent items stored
        private int maxObjects = 20;

        // UI elements for reordering lists in the Editor
        private ReorderableList recentList;
        private ReorderableList favoriteList;

        // Scroll positions for the two lists and main view
        private Vector2 recentScrollPos;
        private Vector2 favoriteScrollPos;
        private Vector2 globalScrollPos;

        // Tracks whether the tool should auto-store newly selected assets
        private bool isStoringEnabled = false;

        // Used to defer object removal from the lists
        private Object recentToRemove = null;
        private Object favoriteToRemove = null;

        // Wrapper class used for serializing lists to EditorPrefs
        [System.Serializable]
        private class ObjectListWrapper
        {
            public List<Object> objects = new List<Object>();
        }

        // Adds the window to the Unity menu
        [MenuItem("Tools/Julien Noe/Object Manager")]
        public static void ShowWindow()
        {
            GetWindow<ObjectManagerEditor>("Object Manager");
        }

        // Called when the window is opened or scripts reload
        private void OnEnable()
        {
            LoadData();      // Load saved data from EditorPrefs
            SetupLists();    // Setup reorderable UI lists
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // Called when the window is closed or scripts reload
        private void OnDisable()
        {
            SaveData();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        // Cleans up data after exiting Play mode
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                Cleanup();         // Remove null entries after Play mode
                SetupLists();      // Refresh ReorderableList bindings
                Repaint();         // Force UI redraw
            }
        }

        // Initializes ReorderableLists for both Recent and Favorite sections
        private void SetupLists()
        {
            recentList = new ReorderableList(recentObjects, typeof(Object), true, false, false, false);
            recentList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                rect.y += 2;
                EditorGUI.ObjectField(new Rect(rect.x, rect.y, rect.width - 60, EditorGUIUtility.singleLineHeight),
                    recentObjects[index], typeof(Object), true);
                if (GUI.Button(new Rect(rect.x + rect.width - 60, rect.y, 60, EditorGUIUtility.singleLineHeight), "Remove"))
                {
                    recentToRemove = recentObjects[index];
                }
            };

            favoriteList = new ReorderableList(favoriteObjects, typeof(Object), true, false, false, false);
            favoriteList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                rect.y += 2;
                EditorGUI.ObjectField(new Rect(rect.x, rect.y, rect.width - 60, EditorGUIUtility.singleLineHeight),
                    favoriteObjects[index], typeof(Object), true);
                if (GUI.Button(new Rect(rect.x + rect.width - 60, rect.y, 60, EditorGUIUtility.singleLineHeight), "Remove"))
                {
                    favoriteToRemove = favoriteObjects[index];
                }
            };
        }

        // Draws the main user interface of the tool
        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(showHelp ? "Close Help ▲" : "Help ▼", GUILayout.Width(100)))
            {
                showHelp = !showHelp;
            }
            GUILayout.EndHorizontal();

            if (showHelp)
            {
                GUIStyle helpBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    normal = { textColor = Color.white, background = Texture2D.grayTexture },
                    wordWrap = true,
                    fontSize = 11,
                    padding = new RectOffset(10, 10, 10, 10)
                };

                string helpText =
                    "-- Favorite Assets Tool" + "This tool helps you manage favorite and recently selected assets in your project." +
                    "★ Favorites:" + "• Drag & drop any asset into the green area." + "• Use 'Reset Favorites' to clear the list." + "-- Recents:" +
                    "• When 'Start Storing' is active, every selected asset is tracked." + "• Limit tracked items with 'Max number of recents'." +
                    "• 'Reset Recent' clears the recent list." + "- Start Storing / - Stop Storing:" + "Toggle automatic tracking of selected assets." +
                    "-- Remove:" + "Click 'Remove' to delete individual items from either list.";

                EditorGUILayout.BeginVertical(helpBoxStyle);
                GUILayout.Label(helpText, helpBoxStyle, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode active: history tracking is disabled.", MessageType.Info);
                GUI.enabled = false; // Disable interaction during Play Mode
            }

            globalScrollPos = EditorGUILayout.BeginScrollView(globalScrollPos);
            EditorGUILayout.Space(10);

            // Favorites Section
            EditorGUILayout.LabelField("Favorites", EditorStyles.boldLabel);
            DrawDropArea("Drop here to add to Favorites", favoriteObjects);
            favoriteScrollPos = EditorGUILayout.BeginScrollView(favoriteScrollPos);
            favoriteList.DoLayoutList();
            EditorGUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("Reset Favorites"))
            {
                favoriteObjects.Clear();
                SaveData();
                SetupLists();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(15);

            // Recent Section
            EditorGUILayout.LabelField("Recent", EditorStyles.boldLabel);
            DrawDropArea("Drop here to add to Recents", recentObjects);
            recentScrollPos = EditorGUILayout.BeginScrollView(recentScrollPos);
            recentList.DoLayoutList();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5);
            int newMaxObjects = EditorGUILayout.DelayedIntField("Max number of recents:", maxObjects);
            if (newMaxObjects != maxObjects)
            {
                maxObjects = newMaxObjects;
                TrimList();
                SaveData();
            }

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("Reset Recent"))
            {
                recentObjects.Clear();
                SaveData();
                SetupLists();
            }

            if (isStoringEnabled)
            {
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Stop Storing"))
                {
                    isStoringEnabled = false;
                }
            }
            else
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("Start Storing"))
                {
                    isStoringEnabled = true;
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            GUI.enabled = true;

            if (recentToRemove != null)
            {
                recentObjects.Remove(recentToRemove);
                recentToRemove = null;
                SaveData();
                Repaint();
            }

            if (favoriteToRemove != null)
            {
                favoriteObjects.Remove(favoriteToRemove);
                favoriteToRemove = null;
                SaveData();
                Repaint();
            }
        }

        // Defines a green drop zone for drag-and-drop asset addition
        private void DrawDropArea(string label, List<Object> targetList)
        {
            Event evt = Event.current;
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);

            GUIStyle centeredStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };
            GUI.Box(dropArea, label, centeredStyle);
            GUI.backgroundColor = originalColor;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        break;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            if (!targetList.Contains(draggedObject))
                            {
                                targetList.Add(draggedObject);
                            }
                        }
                        TrimList();
                        SaveData();
                    }
                    break;
            }
        }

        // Ensures the recent list does not exceed the configured maximum
        private void TrimList()
        {
            while (recentObjects.Count > maxObjects)
            {
                recentObjects.RemoveAt(0);
            }
        }

        // Automatically called when the user selects a new object in the editor
        private void OnSelectionChange()
        {
            if (!isStoringEnabled || Selection.activeObject == null)
                return;

            if (!recentObjects.Contains(Selection.activeObject))
            {
                recentObjects.Add(Selection.activeObject);
                TrimList();
                SaveData();
                Repaint();
            }
        }

        // Saves both lists to EditorPrefs as JSON
        private void SaveData()
        {
            var recentWrapper = new ObjectListWrapper { objects = recentObjects };
            var favWrapper = new ObjectListWrapper { objects = favoriteObjects };

            EditorPrefs.SetString("ObjectManagerEditor_Recent", JsonUtility.ToJson(recentWrapper));
            EditorPrefs.SetString("ObjectManagerEditor_Favorites", JsonUtility.ToJson(favWrapper));
        }

        // Loads data from EditorPrefs and restores lists
        private void LoadData()
        {
            if (EditorPrefs.HasKey("ObjectManagerEditor_Recent"))
            {
                string json = EditorPrefs.GetString("ObjectManagerEditor_Recent");
                var wrapper = JsonUtility.FromJson<ObjectListWrapper>(json);
                if (wrapper != null)
                    recentObjects = wrapper.objects;
            }

            if (EditorPrefs.HasKey("ObjectManagerEditor_Favorites"))
            {
                string json = EditorPrefs.GetString("ObjectManagerEditor_Favorites");
                var wrapper = JsonUtility.FromJson<ObjectListWrapper>(json);
                if (wrapper != null)
                    favoriteObjects = wrapper.objects;
            }

            Cleanup();
        }

        // Cleans the list from null entries (e.g., deleted assets or prefabs in play mode)
        private void Cleanup()
        {
            recentObjects = recentObjects.Where(o => o != null).ToList();
            favoriteObjects = favoriteObjects.Where(o => o != null).ToList();
        }
    }
}

