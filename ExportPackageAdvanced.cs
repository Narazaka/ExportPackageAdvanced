using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using UnityEditor.IMGUI.Controls;

namespace Narazaka.Unity.ExportPackageAdvanced
{
    public class ExportPackageAdvanced : EditorWindow
    {
        [MenuItem("Assets/Export Package (Advanced) ...", priority = 20)]
        static void ShowWindow()
        {
            GetWindow<ExportPackageAdvanced>("Export Package (Advanced)").SetEntryObjects();
        }

        [SerializeField, HideInInspector] Object[] _entryObjects;
        [SerializeField] bool _includeDependencies = true;
        [SerializeField] bool _includePackages;
        [SerializeField] bool _includeScripts;
        [SerializeField] bool _includeShaders = true;
        Asset[] _assets;
        TreeViewState _treeViewState = new TreeViewState();
        Asset[] _filteredAssets = null;
        ExporterTreeView _treeView = null;
        Vector2 _scrollPosition;

        void SetEntryObjects()
        {
            _entryObjects = Selection.GetFiltered<Object>(SelectionMode.DeepAssets);
            _assets = null;
        }

        void OnGUI()
        {
            EditorGUI.LabelField(EditorGUILayout.GetControlRect(GUILayout.Height(EditorGUIUtility.singleLineHeight * 2.5f)), "Items to Export", EditorStyles.boldLabel);
            var color = GUI.color;
            GUI.color = Color.black;
            GUI.Box(EditorGUILayout.GetControlRect(GUILayout.Height(1f)), GUIContent.none);
            GUI.color = color;
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", GUILayout.Width(50)) && _treeView != null) _treeView.IncludeAll();
                if (GUILayout.Button("None", GUILayout.Width(50)) && _treeView != null) _treeView.IncludeNone();
            }
            EditorGUILayout.Space();
            using (new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                if (_treeView == null)
                {
                    _treeView = new ExporterTreeView(_treeViewState);
                }
                if (_assets == null)
                {
                    _assets = GetAssets(_entryObjects, _includeDependencies).OrderBy(asset => asset.path).ToArray();
                    _filteredAssets = null;
                }
                if (_filteredAssets == null)
                {
                    _filteredAssets = _assets
                        .Where(asset => _includeScripts || !asset.isScriptLike)
                        .Where(asset => _includeShaders || !asset.isShaderLike)
                        .Where(asset => _includePackages || !asset.isPackageContent)
                        .ToArray();
                    _treeView.Setup(_filteredAssets);
                    _treeView.ExpandAll();
                }
                if (_filteredAssets.Length == 0)
                {
                    EditorGUILayout.HelpBox("No assets selected.", MessageType.Info);
                }
                else if (_treeView != null)
                {
                    _treeView.OnGUI(EditorGUILayout.GetControlRect(GUILayout.ExpandHeight(true)));
                }
            }
            EditorGUILayout.Space();

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _includeDependencies = EditorGUILayout.ToggleLeft("Include Dependencies", _includeDependencies);
                if (check.changed)
                {
                    _assets = null;
                }
            }
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _includePackages = EditorGUILayout.ToggleLeft("Include Packages", _includePackages);
                if (check.changed)
                {
                    _filteredAssets = null;
                }
            }
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _includeScripts = EditorGUILayout.ToggleLeft("Include Scripts", _includeScripts);
                if (check.changed)
                {
                    _filteredAssets = null;
                }
            }
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _includeShaders = EditorGUILayout.ToggleLeft("Include Shaders", _includeShaders);
                if (check.changed)
                {
                    _filteredAssets = null;
                }
            }
            if (GUILayout.Button("Export...", GUILayout.Width(55)))
            {
                DoExport();
            }
            EditorGUILayout.Space();
        }

        void DoExport()
        {
            var exportAssets = _treeView.IncludedAssets().ToArray();
            if (exportAssets.Length == 0)
            {
                EditorUtility.DisplayDialog("Export Package", "No assets selected.", "OK");
                return;
            }
            var packagePath = EditorUtility.SaveFilePanel("Export Package", "", "", "unitypackage");
            if (!string.IsNullOrEmpty(packagePath))
            {
                AssetDatabase.ExportPackage(exportAssets.Select(asset => asset.path).ToArray(), packagePath, ExportPackageOptions.Interactive);
                Close();
            }
        }

        public static IEnumerable<Asset> GetAssets(Object[] objects, bool withDependencies)
        {
            var allObjects = withDependencies ? objects.Concat(EditorUtility.CollectDependencies(objects)) : objects;
            return allObjects.Where(obj => obj != null).Select(obj => new Asset(obj)).Where(asset => !_ignorePathes.Contains(asset.path)).Distinct(new SamePathAssetComparator());
        }

        static HashSet<string> _ignorePathes = new HashSet<string> { "Resources/unity_builtin_extra", "Library/unity default resources" };

        public class Asset
        {
            public Object obj { get; }
            public string path { get; }
            public bool isPackageContent { get; }
            public bool isScript => obj is MonoScript;
            public bool isDll { get; }
            public bool isAssemblyDefinition => obj is UnityEditorInternal.AssemblyDefinitionAsset;
            public bool isScriptLike => isScript || isDll || isAssemblyDefinition;
            public bool isFolder { get; }
            public bool isShader => obj is Shader;
#if UNITY_2021_2_OR_NEWER
            public bool isShaderInclude => obj is ShaderInclude;
#else
            public bool isShaderInclude => path.EndsWith(".cginc");
#endif
            public bool isShaderVariant => obj is ShaderVariantCollection;
            public bool isShaderLike => isShader || isShaderInclude || isShaderVariant;

            public Asset(Object obj)
            {
                this.obj = obj;
                path = AssetDatabase.GetAssetPath(obj);
                isPackageContent = path.StartsWith("Packages/");
                var isDefaultAsset = obj is DefaultAsset;
                isDll = isDefaultAsset && path.EndsWith(".dll");
                isFolder = isDefaultAsset && AssetDatabase.IsValidFolder(path);
            }
        }

        class SamePathAssetComparator : IEqualityComparer<Asset>
        {
            public bool Equals(Asset x, Asset y)
            {
                return x.path == y.path;
            }

            public int GetHashCode(Asset obj)
            {
                return obj.path.GetHashCode();
            }
        }

        class ExporterTreeView : TreeView
        {
            IList<Asset> _assets;

            public ExporterTreeView(TreeViewState state) : base(state)
            {
            }

            public void Setup(IList<Asset> assets)
            {
                _assets = assets;
                Reload();
            }

            public void IncludeAll()
            {
                foreach (var item in rootItem.children)
                {
                    if (item is Item)
                    {
                        (item as Item).SetInclude(true);
                    }
                }
            }

            public void IncludeNone()
            {
                foreach (var item in rootItem.children)
                {
                    if (item is Item)
                    {
                        (item as Item).SetInclude(false);
                    }
                }
            }

            public IEnumerable<Asset> IncludedAssets()
            {
                return rootItem.children.OfType<Item>().SelectMany(item => IncludedAssets(item));
            }

            IEnumerable<Asset> IncludedAssets(Item item)
            {
                if (item.asset != null && item.include)
                {
                    yield return item.asset;
                }
                if (item.children != null)
                {
                    foreach (var child in item.children)
                    {
                        if (child is Item)
                        {
                            foreach (var asset in IncludedAssets(child as Item))
                            {
                                yield return asset;
                            }
                        }
                    }
                }
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = 0, depth = -1, children = new List<TreeViewItem>() };
                var items = new Dictionary<string, Item>();
                var id = 1;
                foreach (var asset in _assets)
                {
                    var pathParts = GetPathParts(asset.path).ToArray();
                    for (var i = 0; i < pathParts.Length; ++i)
                    {
                        var pathPart = pathParts[i];
                        var itemExists = items.TryGetValue(pathPart, out var item);
                        if (!itemExists)
                        {
                            item = new Item { id = id++, depth = i, displayName = System.IO.Path.GetFileNameWithoutExtension(pathPart) };
                            if (pathPart == asset.path)
                            {
                                item.asset = asset;
                            }
                            items.Add(pathPart, item);

                            if (i == 0)
                            {
                                root.AddChild(item);
                            }
                            else
                            {
                                var parentPath = pathParts[i - 1];
                                var parent = items[parentPath];
                                parent.AddChild(item);
                            }
                        }
                    }
                }
                return root;
            }

            IEnumerable<string> GetPathParts(string path) => GetPathPartsReversed(path).Reverse();

            IEnumerable<string> GetPathPartsReversed(string path)
            {
                // canonicalize
                while (!string.IsNullOrEmpty(path))
                {
                    yield return path;
                    path = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                }
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                var item = (Item)args.item;
                var rect = args.rowRect;
                rect.xMin += GetContentIndent(item);
                var toggleRect = rect;
                toggleRect.width = EditorGUIUtility.singleLineHeight;
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    var include = EditorGUI.Toggle(toggleRect, item.include);
                    if (check.changed)
                    {
                        item.SetInclude(include);
                    }
                }
                rect.xMin += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.LabelField(rect, new GUIContent(item.displayName, item.asset == null ? EditorGUIUtility.IconContent("Folder Icon").image : AssetDatabase.GetCachedIcon(item.asset.path)));
            }

            class Item : TreeViewItem
            {
                public bool include { get; set; } = true;
                public Asset asset { get; set; }

                public void SetInclude(bool include)
                {
                    this.include = include;
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            if (child is Item)
                            {
                                (child as Item).SetInclude(include);
                            }
                        }
                    }
                }
            }
        }
    }
}
