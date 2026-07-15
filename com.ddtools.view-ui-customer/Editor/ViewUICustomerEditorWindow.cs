using System;
using System.IO;
using System.Linq;
using Core.Scripts.UI.Universal.ViewUICustomer;
using UnityEditor;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	public class ViewUICustomerEditorWindow : EditorWindow
	{
		private enum WindowTab
		{
			States,
			Generate,
		}

		[SerializeField] private GameObject _targetObject;
		[SerializeField] private ViewUICustomerPreset _preset;
		[SerializeField] private string _generatedName = "NewUI";
		[SerializeField] private DefaultAsset _generatedFolder;
		[SerializeField] private bool _useBaseViewType;

		private readonly ViewUICustomerPreview _preview = new();
		private readonly CompactGameObjectInspector _inspector = new();
		private IViewUICustomerEditor _customer;
		private MonoBehaviour _target;
		private Type _helperType;
		private ViewUICustomerHierarchy _hierarchy;
		private WindowTab _tab;
		private Enum _selectedViewType;
		private ViewUIStateType _selectedStateType = ViewUIStateType.Idle;
		private string _message;
		private MessageType _messageType;

		[MenuItem("Tools/UI/View UI Customer Editor")]
		public static void Open()
		{
			var window = GetWindow<ViewUICustomerEditorWindow>();
			window.titleContent = new GUIContent("View UI Customer");
			window.minSize = new Vector2(1050f, 620f);
			window.TryUseSelection();
			window.Show();
		}

		[MenuItem("CONTEXT/MonoBehaviour/Open View UI Customer Editor", false, 1000)]
		private static void OpenFromContext(MenuCommand command)
		{
			if (command.context is not MonoBehaviour component || component is not IViewUICustomerEditor)
				return;

			var window = GetWindow<ViewUICustomerEditorWindow>();
			window.titleContent = new GUIContent("View UI Customer");
			window.SetTarget(component.gameObject);
			window.Show();
		}

		[MenuItem("CONTEXT/MonoBehaviour/Open View UI Customer Editor", true)]
		private static bool ValidateOpenFromContext(MenuCommand command)
		{
			return command.context is IViewUICustomerEditor;
		}

		private void OnEnable()
		{
			EditorApplication.hierarchyChanged += OnHierarchyChanged;
			Undo.undoRedoPerformed += OnUndoRedo;
			_preview.RepaintRequested = Repaint;
			_hierarchy = new ViewUICustomerHierarchy();
			SetTarget(_targetObject);
		}

		private void OnDisable()
		{
			EditorApplication.hierarchyChanged -= OnHierarchyChanged;
			Undo.undoRedoPerformed -= OnUndoRedo;
			_hierarchy = null;
			_preview.Dispose();
			_preview.RepaintRequested = null;
		}

		private void OnSelectionChange()
		{
			var previewObjects = Selection.gameObjects.Where(_preview.Contains).ToArray();

			if (previewObjects.Length > 0)
			{
				_inspector.SetTargets(previewObjects);
				Repaint();
				return;
			}

			if (TryGetCustomer(Selection.activeGameObject, out _))
				SetTarget(Selection.activeGameObject);
		}

		private void OnGUI()
		{
			_tab = (WindowTab)GUILayout.Toolbar((int)_tab, new[] { "States & Preview", "Generate" });
			EditorGUILayout.Space(6f);

			if (_tab == WindowTab.States)
				DrawStatesTab();
			else
				DrawGeneratorTab();

			if (string.IsNullOrEmpty(_message) == false)
				EditorGUILayout.HelpBox(_message, _messageType);
		}

		private void DrawStatesTab()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUI.BeginChangeCheck();
				var target = EditorGUILayout.ObjectField("Target GameObject", _targetObject, typeof(GameObject), true) as GameObject;

				if (EditorGUI.EndChangeCheck())
					SetTarget(target);

				if (GUILayout.Button("Selection", GUILayout.Width(80f)))
					TryUseSelection();
			}

			if (_customer == null)
			{
				EditorGUILayout.HelpBox("Select a component derived from ViewUICustomer<,>.", MessageType.Info);
				var emptyRect = GUILayoutUtility.GetRect(10f, 10000f, 200f, 10000f, GUILayout.ExpandHeight(true));
				_preview.Draw(emptyRect);
				return;
			}

			DrawTopControls();
			EditorGUILayout.Space(4f);

			var workspaceHeight = Mathf.Max(240f, position.height - 255f);

			using (new EditorGUILayout.HorizontalScope(GUILayout.Height(workspaceHeight)))
			{
				using (new EditorGUILayout.VerticalScope(GUILayout.Width(235f)))
				{
					EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);
					var hierarchyRect = GUILayoutUtility.GetRect(235f, workspaceHeight - 20f, GUILayout.ExpandHeight(false));
					_hierarchy?.Draw(hierarchyRect, _preview.Root);
				}

				using (new EditorGUILayout.VerticalScope())
				{
					EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
					var previewRect = GUILayoutUtility.GetRect(100f, workspaceHeight - 45f, GUILayout.ExpandWidth(true));
					_preview.Draw(previewRect);

					using (new EditorGUILayout.HorizontalScope())
					{
						if (GUILayout.Button("Reset view"))
							_preview.ResetView();

						if (GUILayout.Button("Rebuild"))
							RebuildPreview();
					}
				}

				using (new EditorGUILayout.VerticalScope(GUILayout.Width(400f)))
				{
					EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
					_inspector.Draw(workspaceHeight - 20f, _preview.Root);
				}
			}

		}

		private void DrawTopControls()
		{
			EditorGUILayout.HelpBox(
				"Порядок работы: выберите View Type и State. При необходимости загрузите сохранённое состояние, измените объекты в Preview/Inspector, затем сохраните Preview в выбранное состояние.",
				MessageType.Info);

			using (new EditorGUILayout.HorizontalScope())
			{
				DrawStateControls();
				GUILayout.Space(8f);
				DrawPresetControls();
			}
		}

		private void DrawStateControls()
		{
			using var panel = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
			EditorGUILayout.LabelField("State", EditorStyles.boldLabel);

			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUI.BeginChangeCheck();
				_selectedViewType = EditorGUILayout.EnumPopup("View type", _selectedViewType);
				_selectedStateType = (ViewUIStateType)EditorGUILayout.EnumPopup("State", _selectedStateType);

				if (EditorGUI.EndChangeCheck())
					SetMessage("Состояние выбрано. Загрузите сохранённые значения или отредактируйте текущий Preview и сохраните его в это состояние.", MessageType.Info);
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button(new GUIContent(
					"Загрузить",
					"Загружает значения выбранных View Type и State. Текущие изменения Preview при этом не сохраняются."),
					GUILayout.Width(120f)))
					LoadSelectedState();

				if (GUILayout.Button(new GUIContent(
					"Сохранить",
					"Сохраняет текущие значения объектов Preview в выбранные View Type и State."),
					GUILayout.Width(120f)))
					CapturePreviewState();

				if (GUILayout.Button(new GUIContent(
					"Копировать из… ▾",
					"Применяет выбранное сохранённое состояние ко всему Preview, не меняя целевые View Type и State."),
					GUILayout.Width(145f)))
					ShowCopyStateMenu();

				GUILayout.FlexibleSpace();
			}
		}

		private void ShowCopyStateMenu()
		{
			var menu = new GenericMenu();
			var data = _preview.CaptureStoredStates();
			int itemCount = 0;

			if (data?.States != null && _customer != null)
			{
				foreach (var state in data.States)
				{
					if (state?.Groups == null || string.IsNullOrEmpty(state.ViewType))
						continue;

					foreach (var group in state.Groups)
					{
						if (Enum.IsDefined(typeof(ViewUIStateType), group.StateType) == false)
							continue;

						var sourceViewType = Enum.Parse(_customer.EditorViewType, state.ViewType) as Enum;
						var sourceStateType = (ViewUIStateType)group.StateType;
						string path = $"{state.ViewType}/{sourceStateType}";
						menu.AddItem(new GUIContent(path), false, () => CopyWholeStateToPreview(sourceViewType, sourceStateType));
						itemCount++;
					}
				}
			}

			if (itemCount == 0)
				menu.AddDisabledItem(new GUIContent("Нет сохранённых состояний"));

			menu.DropDown(GUILayoutUtility.GetLastRect());
		}

		private void CopyWholeStateToPreview(Enum sourceViewType, ViewUIStateType sourceStateType)
		{
			bool result = _preview.ApplyState(sourceViewType, sourceStateType);
			SetMessage(
				result
					? $"Состояние {sourceViewType}/{sourceStateType} скопировано в Preview. Целевое состояние осталось {_selectedViewType}/{_selectedStateType}."
					: $"Не удалось загрузить состояние {sourceViewType}/{sourceStateType}.",
				result ? MessageType.Info : MessageType.Error);
		}

		private void DrawPresetControls()
		{
			using var panel = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(510f));
			EditorGUILayout.LabelField("Preset / JSON", EditorStyles.boldLabel);
			_preset = EditorGUILayout.ObjectField("Preset", _preset, typeof(ViewUICustomerPreset), false, GUILayout.Width(490f)) as ViewUICustomerPreset;

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Создать asset", GUILayout.Width(110f)))
					SavePresetAsset();

				using (new EditorGUI.DisabledScope(_preset == null))
				{
					if (GUILayout.Button("Загрузить", GUILayout.Width(100f)))
						ApplyPreset(_preset.Data);
				}

				if (GUILayout.Button("Export JSON", GUILayout.Width(100f)))
					ExportJson();

				if (GUILayout.Button("Import JSON", GUILayout.Width(100f)))
					ImportJson();

				GUILayout.FlexibleSpace();
			}
		}

		private void DrawGeneratorTab()
		{
			EditorGUILayout.LabelField("Generate ViewUICustomer scripts", EditorStyles.boldLabel);
			_generatedName = EditorGUILayout.TextField("Name", _generatedName);
			_generatedFolder = EditorGUILayout.ObjectField("Folder", _generatedFolder, typeof(DefaultAsset), false) as DefaultAsset;
			_useBaseViewType = EditorGUILayout.ToggleLeft("Use shared ViewUIBaseType instead of generating a ViewType enum", _useBaseViewType);

			var folderPath = _generatedFolder == null ? string.Empty : AssetDatabase.GetAssetPath(_generatedFolder);
			var namespaceName = string.IsNullOrEmpty(folderPath) ? "-" : ViewUICustomerCodeGenerator.GetNamespace(folderPath);
			EditorGUILayout.LabelField("Namespace", namespaceName);
			EditorGUILayout.Space(6f);
			EditorGUILayout.HelpBox(
				"The generator creates View, GetHelper and ElementType. ElementType starts with None = -1; named values are generated when GetHelper is assigned to objects. ViewType starts with Default = 0 unless the shared base type is selected.",
				MessageType.Info);

			using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_generatedName) || string.IsNullOrEmpty(folderPath)))
			{
				if (GUILayout.Button("Generate scripts", GUILayout.Height(32f)))
				{
					bool result = ViewUICustomerCodeGenerator.Generate(_generatedName, folderPath, _useBaseViewType, out var error);
					SetMessage(result ? "Scripts generated. Unity is compiling the new types." : error, result ? MessageType.Info : MessageType.Error);
				}
			}
		}

		private void CapturePreviewState()
		{
			if (_preview.TryCaptureState(_selectedViewType, _selectedStateType, out var data, out var captureError) == false)
			{
				SetMessage(captureError ?? "Не удалось сохранить состояние Preview.", MessageType.Error);
				return;
			}

			bool result = SavePreviewStateToSource(data, out var error);

			if (result)
				RebuildPreview();

			SetMessage(error ?? (result ? "Preview сохранён в выбранное состояние." : "Не удалось сохранить состояние Preview."), result ? MessageType.Info : MessageType.Error);
		}

		private bool SavePreviewStateToSource(ViewUICustomerPresetData data, out string error)
		{
			if (EditorUtility.IsPersistent(_targetObject) == false)
			{
				ViewUICustomerHelperUtility.SyncRootRectSize(_preview.Root, _targetObject);

				if (ViewUICustomerHelperUtility.SyncHelpersToSource(
					_preview.Root,
					_targetObject,
					_helperType,
					out error) == false)
					return false;

				return ViewUICustomerPresetSerializer.Apply(_target, _customer, data, out error);
			}

			string prefabPath = AssetDatabase.GetAssetPath(_targetObject);
			var assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

			if (assetRoot == null)
			{
				error = "Не удалось загрузить исходный prefab asset.";
				return false;
			}

			string sourcePath = _targetObject == assetRoot
				? string.Empty
				: AnimationUtility.CalculateTransformPath(_targetObject.transform, assetRoot.transform);
			string componentPath = AnimationUtility.CalculateTransformPath(_target.transform, _targetObject.transform);
			Type componentType = _target.GetType();
			var loadedRoot = PrefabUtility.LoadPrefabContents(prefabPath);

			try
			{
				var loadedSource = FindGameObject(loadedRoot, sourcePath);

				if (loadedSource == null)
				{
					error = "Не удалось найти выбранный root внутри загруженного prefab asset.";
					return false;
				}

				var componentObject = FindGameObject(loadedSource, componentPath);
				var loadedTarget = FindCustomer(componentObject, componentType);

				if (loadedTarget == null || loadedTarget is not IViewUICustomerEditor loadedCustomer)
				{
					error = "Не удалось найти ViewUICustomer внутри загруженного prefab asset.";
					return false;
				}

				ViewUICustomerHelperUtility.SyncRootRectSize(_preview.Root, loadedSource);
				ViewUICustomerHelperUtility.SyncDirect(_preview.Root, loadedSource, _helperType);

				if (ViewUICustomerPresetSerializer.Apply(loadedTarget, loadedCustomer, data, out error) == false)
					return false;

				PrefabUtility.SaveAsPrefabAsset(loadedRoot, prefabPath);
			}
			catch (Exception exception)
			{
				error = $"Не удалось сохранить prefab asset: {exception.Message}";
				return false;
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(loadedRoot);
			}

			AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);
			assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			_targetObject = FindGameObject(assetRoot, sourcePath);
			var refreshedComponentObject = FindGameObject(_targetObject, componentPath);
			_target = FindCustomer(refreshedComponentObject, componentType);
			_customer = _target as IViewUICustomerEditor;

			if (_customer == null)
			{
				error = "Prefab сохранён, но не удалось обновить ссылку на ViewUICustomer.";
				return false;
			}

			error = null;
			return true;
		}

		private static GameObject FindGameObject(GameObject root, string path)
		{
			if (root == null)
				return null;

			return string.IsNullOrEmpty(path) ? root : root.transform.Find(path)?.gameObject;
		}

		private static MonoBehaviour FindCustomer(GameObject gameObject, Type componentType)
		{
			if (gameObject == null)
				return null;

			foreach (var behaviour in gameObject.GetComponents<MonoBehaviour>())
			{
				if (behaviour != null && behaviour.GetType() == componentType && behaviour is IViewUICustomerEditor)
					return behaviour;
			}

			return null;
		}

		private void SavePresetAsset()
		{
			var path = EditorUtility.SaveFilePanelInProject(
				"Save View UI Customer preset",
				$"{_targetObject.name}Preset",
				"asset",
				"Choose where to save the preset asset.");

			if (string.IsNullOrEmpty(path))
				return;

			var preset = CreateInstance<ViewUICustomerPreset>();
			preset.Data = ViewUICustomerPresetSerializer.Capture(_target, _customer);
			AssetDatabase.CreateAsset(preset, path);
			AssetDatabase.SaveAssets();
			_preset = preset;
			Selection.activeObject = preset;
			SetMessage("Preset asset saved.", MessageType.Info);
		}

		private void ExportJson()
		{
			var path = EditorUtility.SaveFilePanel("Export View UI Customer JSON", string.Empty, $"{_targetObject.name}.json", "json");

			if (string.IsNullOrEmpty(path))
				return;

			var data = ViewUICustomerPresetSerializer.Capture(_target, _customer);
			File.WriteAllText(path, JsonUtility.ToJson(data, true));
			SetMessage("JSON exported.", MessageType.Info);
		}

		private void ImportJson()
		{
			var path = EditorUtility.OpenFilePanel("Import View UI Customer JSON", string.Empty, "json");

			if (string.IsNullOrEmpty(path))
				return;

			try
			{
				var data = JsonUtility.FromJson<ViewUICustomerPresetData>(File.ReadAllText(path));
				ApplyPreset(data);
			}
			catch (Exception exception)
			{
				SetMessage($"JSON import failed: {exception.Message}", MessageType.Error);
			}
		}

		private void ApplyPreset(ViewUICustomerPresetData data)
		{
			bool result = ViewUICustomerPresetSerializer.Apply(_target, _customer, data, out var error);

			if (result)
				RebuildPreview();

			SetMessage(error ?? "Preset applied.", result && string.IsNullOrEmpty(error) ? MessageType.Info : MessageType.Warning);
		}

		private void ApplyPreviewState()
		{
			_preview.ApplyState(_selectedViewType, _selectedStateType);
			Repaint();
		}

		private void LoadSelectedState()
		{
			bool result = _preview.ApplyState(_selectedViewType, _selectedStateType);
			SetMessage(
				result
					? "Сохранённое состояние загружено в Preview. Теперь его можно редактировать и сохранить."
					: "Это состояние ещё не сохранено. Отредактируйте Preview и нажмите «Сохранить Preview в состояние».",
				result ? MessageType.Info : MessageType.Warning);
			Repaint();
		}

		private void SetTarget(GameObject targetObject)
		{
			MonoBehaviour target = null;

			if (targetObject != null && TryGetCustomer(targetObject, out target) == false)
			{
				SetMessage("The selected GameObject has no component derived from ViewUICustomer<,>.", MessageType.Warning);
				targetObject = null;
			}

			_targetObject = targetObject;
			_target = target;
			_customer = target as IViewUICustomerEditor;
			_helperType = _customer == null
				? null
				: ViewUICustomerHelperUtility.ResolveHelperType(_target.GetType(), _customer.EditorElementType);
			_hierarchy?.Configure(
				_helperType,
				_customer?.EditorElementType,
				OnHelpersChanged,
				_preview.CaptureStoredStates,
				OnElementStateApplied);
			InitializeSelection();
			_preview.SetTarget(_targetObject, _target);
			_hierarchy?.SetRoot(_preview.Root);
			SelectPreviewRoot();
			ApplyPreviewState();
		}

		private void RebuildPreview()
		{
			_preview.SetTarget(_targetObject, _target);
			_hierarchy?.SetRoot(_preview.Root);
			SelectPreviewRoot();
			ApplyPreviewState();
		}

		private void InitializeSelection()
		{
			_selectedViewType = null;

			if (_customer == null)
				return;

			foreach (Enum value in Enum.GetValues(_customer.EditorViewType))
			{
				if (Convert.ToInt32(value) < 0)
					continue;

				_selectedViewType = value;
				break;
			}
		}

		private void TryUseSelection()
		{
			if (TryGetCustomer(Selection.activeGameObject, out _))
				SetTarget(Selection.activeGameObject);
			else
				SetMessage("The active selection has no ViewUICustomer component.", MessageType.Info);
		}

		private static bool TryGetCustomer(GameObject gameObject, out MonoBehaviour component)
		{
			component = null;

			if (gameObject == null)
				return false;

			foreach (var behaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (behaviour is not IViewUICustomerEditor)
					continue;

				component = behaviour;
				return true;
			}

			return false;
		}

		private void SelectPreviewRoot()
		{
			if (_preview.Root == null)
			{
				_inspector.SetTarget(null);
				return;
			}

			Selection.activeGameObject = _preview.Root;
			_inspector.SetTargets(new[] { _preview.Root });
		}

		private void OnHierarchyChanged()
		{
			Repaint();
		}

		private void OnUndoRedo()
		{
			_inspector.SetTargets(Selection.gameObjects.Where(_preview.Contains));
			Repaint();
		}

		private void OnHelpersChanged()
		{
			bool result = ViewUICustomerHelperUtility.SyncHelpersToSource(
				_preview.Root,
				_targetObject,
				_helperType,
				out var error);

			SetMessage(
				result ? "GetHelper-маркировка сохранена в исходный объект." : error,
				result ? MessageType.Info : MessageType.Error);
			Repaint();
		}

		private void OnElementStateApplied(GameObject gameObject, ViewUICustomerElementData data)
		{
			bool result = _preview.ApplyElementState(gameObject, data, out var error);
			SetMessage(
				result ? "Состояние выбранных элементов скопировано в Preview. Для записи в выбранное состояние нажмите «Сохранить Preview в состояние»." : error,
				result ? MessageType.Info : MessageType.Error);
		}

		private void SetMessage(string message, MessageType messageType)
		{
			_message = message;
			_messageType = messageType;
			Repaint();
		}
	}
}
