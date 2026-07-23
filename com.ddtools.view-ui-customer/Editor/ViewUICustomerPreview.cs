using Core.Scripts.UI.Universal.ViewUICustomer;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Core.EditorTools.ViewUICustomer
{
	internal sealed class ViewUICustomerPreview
	{
		private const float BaseZoom = 0.2f;
		private const float MinDisplayedZoom = 0.2f;
		private const float MaxDisplayedZoom = 5f;

		private PreviewRenderUtility _previewUtility;
		private GameObject _previewObject;
		private MonoBehaviour _previewComponent;
		private IViewUICustomerEditor _previewCustomer;
		private float _zoom = BaseZoom;
		private Vector2 _pan;
		private Vector2 _contentSize = new(800f, 600f);
		private RectTransform _resizeTarget;
		private int _resizeHandleIndex = -1;
		private Vector3 _resizeStartMouseWorld;
		private Vector3 _resizeMouseOffsetWorld;
		private Vector2 _resizeStartSizeDelta;
		private Vector2 _resizeStartRectSize;
		private Vector2 _resizeStartAnchoredPosition;
		private RectTransform _moveTarget;
		private Vector3 _moveStartMouseWorld;
		private Vector2 _moveStartAnchoredPosition;
		private float? _snapGuideX;
		private float? _snapGuideY;
		private bool _transitionRunning;
		private double _transitionStartedAt;
		private float _transitionDuration;

		public Scene Scene => _previewUtility == null ? default : _previewUtility.camera.scene;
		public GameObject Root => _previewObject;
		public System.Action RepaintRequested { get; set; }

		public void SetTarget(GameObject targetRoot, MonoBehaviour target)
		{
			FinishEditorTransition();
			DestroyPreviewObject();

			if (targetRoot == null || target == null)
				return;

			EnsurePreviewUtility();
			var customerPath = AnimationUtility.CalculateTransformPath(target.transform, targetRoot.transform);
			_previewObject = Object.Instantiate(targetRoot);
			_previewObject.name = $"{targetRoot.name} (ViewUICustomer Preview)";
			_previewObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
			_previewObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
			_previewObject.transform.localScale = Vector3.one;
			_previewObject.SetActive(true);

			var customerTransform = string.IsNullOrEmpty(customerPath)
				? _previewObject.transform
				: _previewObject.transform.Find(customerPath);

			if (customerTransform != null)
			{
				foreach (var behaviour in customerTransform.GetComponents<MonoBehaviour>())
				{
					if (behaviour.GetType() != target.GetType() || behaviour is not IViewUICustomerEditor customer)
						continue;

					_previewComponent = behaviour;
					_previewCustomer = customer;
					break;
				}
			}

			ConfigureCanvas();
			_previewUtility.AddSingleGO(_previewObject);

			foreach (var transform in _previewObject.GetComponentsInChildren<Transform>(true))
				transform.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

			EnsureRootAtOrigin();
		}

		public bool ApplyState(System.Enum viewType, ViewUIStateType stateType)
		{
			if (_previewCustomer == null || viewType == null)
				return false;

			if (_previewCustomer.EditorHasState(viewType, stateType) == false)
				return false;

			FinishEditorTransition();
			bool result = _previewCustomer.EditorBeginStateTransition(viewType, stateType);
			_transitionDuration = Mathf.Max(0f, _previewCustomer.EditorTransitionDuration);

			if (_transitionDuration > 0f)
			{
				_transitionRunning = true;
				_transitionStartedAt = EditorApplication.timeSinceStartup;
				EditorApplication.update -= UpdateEditorTransition;
				EditorApplication.update += UpdateEditorTransition;
				_previewCustomer.EditorApplyTransition(0f);
			}
			else
			{
				_previewCustomer.EditorCompleteTransition();
			}

			EnsureRootAtOrigin();
			return result;
		}

		public bool TryCaptureState(
			System.Enum viewType,
			ViewUIStateType stateType,
			out ViewUICustomerPresetData data,
			out string error)
		{
			FinishEditorTransition();
			data = null;
			error = null;

			if (_previewComponent == null || _previewCustomer == null || viewType == null)
			{
				error = "Preview не инициализирован.";
				return false;
			}

			if (_previewCustomer.EditorValidateCapture(out error) == false)
				return false;

			bool isValid = _previewCustomer.EditorCaptureState(viewType, stateType);
			data = ViewUICustomerPresetSerializer.Capture(_previewComponent, _previewCustomer);

			if (isValid == false && string.IsNullOrEmpty(error))
				error = "Состояние содержит некорректные настройки элементов.";

			return isValid;
		}

		public ViewUICustomerPresetData CaptureStoredStates()
		{
			return _previewComponent == null || _previewCustomer == null
				? null
				: ViewUICustomerPresetSerializer.Capture(_previewComponent, _previewCustomer);
		}

		public bool ApplyElementState(GameObject gameObject, ViewUICustomerElementData data, out string error)
		{
			FinishEditorTransition();
			error = null;

			if (gameObject == null || data == null || Contains(gameObject) == false)
			{
				error = "Не удалось применить состояние элемента: объект не принадлежит текущему Preview.";
				return false;
			}

			Undo.RecordObject(gameObject, "Copy View UI element state");
			gameObject.SetActive(data.IsActive);

			if (gameObject.transform is RectTransform rectTransform)
			{
				Undo.RecordObject(rectTransform, "Copy View UI element state");
				if (gameObject == _previewObject)
					rectTransform.anchoredPosition3D = Vector3.zero;
				else if (data.UsesAnchoredPosition)
					rectTransform.anchoredPosition3D = data.LocalPosition;
				else
					rectTransform.localPosition = data.LocalPosition;
				rectTransform.sizeDelta = data.SizeDelta;
			}

			if (gameObject.TryGetComponent(out Graphic graphic))
			{
				Undo.RecordObject(graphic, "Copy View UI element state");
				graphic.color = data.Color;

				if (graphic is Image image)
				{
					string materialPath = string.IsNullOrEmpty(data.MaterialGuid)
						? string.Empty
						: AssetDatabase.GUIDToAssetPath(data.MaterialGuid);
					image.material = string.IsNullOrEmpty(materialPath)
						? null
						: AssetDatabase.LoadAssetAtPath<Material>(materialPath);
				}
			}

			ViewUICustomerPresetSerializer.ApplyElementProperties(_previewComponent, gameObject, data);

			EnsureRootAtOrigin();
			return true;
		}

		public bool Contains(GameObject gameObject)
		{
			return gameObject != null
				&& _previewObject != null
				&& (gameObject == _previewObject || gameObject.transform.IsChildOf(_previewObject.transform));
		}

		public void Draw(Rect rect)
		{
			EnsureRootAtOrigin();

			if (Event.current.type == EventType.ScrollWheel && rect.Contains(Event.current.mousePosition))
			{
				_zoom = Mathf.Clamp(
					_zoom * (1f - Event.current.delta.y * 0.04f),
					BaseZoom * MinDisplayedZoom,
					BaseZoom * MaxDisplayedZoom);
				Event.current.Use();
			}

			if (Event.current.type == EventType.MouseDrag && Event.current.button == 2 && rect.Contains(Event.current.mousePosition))
			{
				float worldUnitsPerPixel = _contentSize.y / Mathf.Max(1f, rect.height * _zoom);
				_pan += Event.current.delta * worldUnitsPerPixel;
				Event.current.Use();
			}

			if (_previewUtility == null || _previewObject == null)
			{
				EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
				GUI.Label(rect, "Select a ViewUICustomer component or prefab", EditorStyles.centeredGreyMiniLabel);
				return;
			}

			_previewUtility.BeginPreview(rect, GUIStyle.none);
			var camera = _previewUtility.camera;
			camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
			camera.clearFlags = CameraClearFlags.Color;
			camera.orthographic = true;
			camera.orthographicSize = Mathf.Max(1f, _contentSize.y * 0.5f / _zoom);
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane = 2000f;
			camera.transform.position = new Vector3(-_pan.x, _pan.y, -1000f);
			camera.transform.rotation = Quaternion.identity;
			camera.Render();
			var texture = _previewUtility.EndPreview();
			GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
			HandlePreviewInteraction(rect);
			DrawSelection(rect);
			float displayedZoom = _zoom / BaseZoom;
			GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 420f, 18f), $"Zoom: {displayedZoom:0.00}x  |  LMB: Rect Tool  |  MMB: pan", EditorStyles.miniLabel);
		}

		public void ResetView()
		{
			_zoom = BaseZoom;
			_pan = Vector2.zero;
		}

		public void Dispose()
		{
			FinishEditorTransition();
			DestroyPreviewObject();
			_previewUtility?.Cleanup();
			_previewUtility = null;
		}

		private void UpdateEditorTransition()
		{
			if (_transitionRunning == false || _previewCustomer == null)
			{
				StopEditorTransitionUpdates();
				return;
			}

			float elapsed = (float)(EditorApplication.timeSinceStartup - _transitionStartedAt);
			float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, _transitionDuration));
			float progress = _previewCustomer.EditorEvaluateTransition(normalizedTime);
			_previewCustomer.EditorApplyTransition(progress);
			EnsureRootAtOrigin();
			RepaintRequested?.Invoke();

			if (normalizedTime >= 1f)
				FinishEditorTransition();
		}

		private void FinishEditorTransition()
		{
			if (_transitionRunning && _previewCustomer != null)
				_previewCustomer.EditorCompleteTransition();

			StopEditorTransitionUpdates();
		}

		private void StopEditorTransitionUpdates()
		{
			_transitionRunning = false;
			EditorApplication.update -= UpdateEditorTransition;
		}

		private void EnsurePreviewUtility()
		{
			if (_previewUtility != null)
				return;

			_previewUtility = new PreviewRenderUtility();
			_previewUtility.cameraFieldOfView = 30f;
		}

		private void ConfigureCanvas()
		{
			var rectTransform = _previewObject.GetComponent<RectTransform>();

			if (rectTransform != null)
			{
				_contentSize = rectTransform.rect.size;

				if (_contentSize.x <= 1f || _contentSize.y <= 1f)
					_contentSize = rectTransform.sizeDelta;
			}

			if (_contentSize.x <= 1f || _contentSize.y <= 1f)
				_contentSize = new Vector2(800f, 600f);

			var canvases = _previewObject.GetComponentsInChildren<Canvas>(true);
			bool hasRootCanvas = false;

			foreach (var canvas in canvases)
			{
				if (canvas.transform != _previewObject.transform && canvas.transform.parent.GetComponentInParent<Canvas>() != null)
					continue;

				canvas.renderMode = RenderMode.WorldSpace;
				canvas.worldCamera = _previewUtility.camera;
				hasRootCanvas = true;
			}

			if (hasRootCanvas == false)
			{
				var canvas = _previewObject.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.WorldSpace;
				canvas.worldCamera = _previewUtility.camera;
			}
		}

		private void DestroyPreviewObject()
		{
			_resizeTarget = null;
			_resizeHandleIndex = -1;
			_moveTarget = null;
			_snapGuideX = null;
			_snapGuideY = null;

			if (_previewObject != null)
				Object.DestroyImmediate(_previewObject);

			_previewObject = null;
			_previewComponent = null;
			_previewCustomer = null;
		}

		private void HandlePreviewInteraction(Rect previewRect)
		{
			var currentEvent = Event.current;

			if (_resizeTarget != null && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
			{
				ResizeSelectedElement(previewRect, currentEvent.mousePosition);
				currentEvent.Use();
				return;
			}

			if (_moveTarget != null && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
			{
				MoveSelectedElement(previewRect, currentEvent.mousePosition);
				currentEvent.Use();
				return;
			}

			if ((_resizeTarget != null || _moveTarget != null) && currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
			{
				if (_resizeTarget != null)
					EditorUtility.SetDirty(_resizeTarget);

				if (_moveTarget != null)
					EditorUtility.SetDirty(_moveTarget);

				_resizeTarget = null;
				_resizeHandleIndex = -1;
				_moveTarget = null;
				_snapGuideX = null;
				_snapGuideY = null;
				currentEvent.Use();
				return;
			}

			if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || previewRect.Contains(currentEvent.mousePosition) == false)
				return;

			var selectedRect = GetSelectedRectTransform();

			if (selectedRect != null && TryGetResizeHandle(previewRect, selectedRect, currentEvent.mousePosition, out var handleIndex))
			{
				_resizeTarget = selectedRect;
				_resizeHandleIndex = handleIndex;
				_resizeStartMouseWorld = GuiToWorld(previewRect, currentEvent.mousePosition);
				var resizeCorners = new Vector3[4];
				selectedRect.GetWorldCorners(resizeCorners);
				_resizeMouseOffsetWorld = _resizeStartMouseWorld - GetHandleWorldPositions(resizeCorners)[handleIndex];
				_resizeStartSizeDelta = selectedRect.sizeDelta;
				_resizeStartRectSize = selectedRect.rect.size;
				_resizeStartAnchoredPosition = selectedRect.anchoredPosition;
				Undo.RegisterCompleteObjectUndo(selectedRect, "Resize preview UI element");
				currentEvent.Use();
				return;
			}

			var mouseWorld = GuiToWorld(previewRect, currentEvent.mousePosition);
			var localPoint = selectedRect == null ? Vector3.zero : selectedRect.InverseTransformPoint(mouseWorld);

			if (selectedRect != null && selectedRect.gameObject != _previewObject && selectedRect.rect.Contains(localPoint))
			{
				_moveTarget = selectedRect;
				_moveStartMouseWorld = mouseWorld;
				_moveStartAnchoredPosition = selectedRect.anchoredPosition;
				Undo.RegisterCompleteObjectUndo(selectedRect, "Move preview UI element");
				currentEvent.Use();
			}
		}

		private void DrawSelection(Rect previewRect)
		{
			if (Event.current.type != EventType.Repaint)
				return;

			var selectedRect = GetSelectedRectTransform();

			if (selectedRect == null)
				return;

			var worldCorners = new Vector3[4];
			selectedRect.GetWorldCorners(worldCorners);
			var handlePositions = GetHandleWorldPositions(worldCorners);
			var guiCorners = new Vector3[5];

			for (int i = 0; i < 4; i++)
			{
				var corner = WorldToGui(previewRect, worldCorners[i]) - previewRect.position;
				guiCorners[i] = new Vector3(corner.x, corner.y, 0f);
			}

			guiCorners[4] = guiCorners[0];
			GUI.BeginClip(previewRect);
			Handles.BeginGUI();
			Handles.color = new Color(0.2f, 0.65f, 1f, 1f);
			Handles.DrawAAPolyLine(2f, guiCorners);
			Handles.EndGUI();
			float minX = Mathf.Min(guiCorners[0].x, guiCorners[1].x, guiCorners[2].x, guiCorners[3].x);
			float maxX = Mathf.Max(guiCorners[0].x, guiCorners[1].x, guiCorners[2].x, guiCorners[3].x);
			float minY = Mathf.Min(guiCorners[0].y, guiCorners[1].y, guiCorners[2].y, guiCorners[3].y);
			float maxY = Mathf.Max(guiCorners[0].y, guiCorners[1].y, guiCorners[2].y, guiCorners[3].y);
			EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(minX, minY, maxX, maxY), MouseCursor.MoveArrow);

			for (int i = 0; i < handlePositions.Length; i++)
			{
				var globalHandlePosition = WorldToGui(previewRect, handlePositions[i]);
				var localHandlePosition = globalHandlePosition - previewRect.position;
				EditorGUI.DrawRect(GetHandleRect(localHandlePosition), new Color(0.2f, 0.65f, 1f, 1f));
				EditorGUIUtility.AddCursorRect(GetHandleRect(localHandlePosition), GetHandleCursor(i));
			}

			if (_snapGuideX.HasValue)
			{
				float guideX = WorldToGui(previewRect, new Vector3(_snapGuideX.Value, 0f, 0f)).x - previewRect.x;
				EditorGUI.DrawRect(new Rect(guideX, 0f, 1f, previewRect.height), new Color(1f, 0.35f, 0.65f, 0.9f));
			}

			if (_snapGuideY.HasValue)
			{
				float guideY = WorldToGui(previewRect, new Vector3(0f, _snapGuideY.Value, 0f)).y - previewRect.y;
				EditorGUI.DrawRect(new Rect(0f, guideY, previewRect.width, 1f), new Color(1f, 0.35f, 0.65f, 0.9f));
			}

			var labelRect = new Rect(guiCorners[1].x + 6f, guiCorners[1].y - 20f, 220f, 18f);
			GUI.Label(labelRect, selectedRect.gameObject.name, EditorStyles.miniLabel);
			GUI.EndClip();
		}

		private RectTransform GetSelectedRectTransform()
		{
			var selectedObject = Selection.activeGameObject;

			return Contains(selectedObject) ? selectedObject.GetComponent<RectTransform>() : null;
		}

		private bool TryGetResizeHandle(Rect previewRect, RectTransform rectTransform, Vector2 mousePosition, out int handleIndex)
		{
			var worldCorners = new Vector3[4];
			rectTransform.GetWorldCorners(worldCorners);
			var handlePositions = GetHandleWorldPositions(worldCorners);

			for (int i = 0; i < handlePositions.Length; i++)
			{
				if (GetHandleRect(WorldToGui(previewRect, handlePositions[i])).Contains(mousePosition) == false)
					continue;

				handleIndex = i;
				return true;
			}

			handleIndex = -1;
			return false;
		}

		private void ResizeSelectedElement(Rect previewRect, Vector2 mousePosition)
		{
			if (_resizeTarget == null || _resizeHandleIndex < 0)
				return;

			var currentMouseWorld = GuiToWorld(previewRect, mousePosition);
			SnapResizeMouse(previewRect, _resizeTarget, ref currentMouseWorld);
			var worldDelta = currentMouseWorld - _resizeStartMouseWorld;
			var localDelta3 = _resizeTarget.InverseTransformVector(worldDelta);
			var localDelta = new Vector2(localDelta3.x, localDelta3.y);
			bool changesX = _resizeHandleIndex is 0 or 1 or 2 or 3 or 4 or 6;
			bool changesY = _resizeHandleIndex is 0 or 1 or 2 or 3 or 5 or 7;
			float signX = _resizeHandleIndex is 0 or 1 or 4 ? -1f : 1f;
			float signY = _resizeHandleIndex is 0 or 3 or 7 ? -1f : 1f;
			var sizeChange = new Vector2(signX * localDelta.x, signY * localDelta.y);

			sizeChange.x = changesX ? Mathf.Max(1f - _resizeStartRectSize.x, sizeChange.x) : 0f;
			sizeChange.y = changesY ? Mathf.Max(1f - _resizeStartRectSize.y, sizeChange.y) : 0f;
			var appliedDrag = new Vector2(changesX ? signX * sizeChange.x : 0f, changesY ? signY * sizeChange.y : 0f);
			var pivotOffset = Vector2.Scale(new Vector2(0.5f - _resizeTarget.pivot.x, 0.5f - _resizeTarget.pivot.y), sizeChange);
			var pivotMovement = appliedDrag * 0.5f - pivotOffset;
			var worldPivotMovement = _resizeTarget.TransformVector(pivotMovement);
			var parentMovement = _resizeTarget.parent == null
				? worldPivotMovement
				: _resizeTarget.parent.InverseTransformVector(worldPivotMovement);

			_resizeTarget.sizeDelta = _resizeStartSizeDelta + sizeChange;
			_resizeTarget.anchoredPosition = _resizeStartAnchoredPosition + new Vector2(parentMovement.x, parentMovement.y);
			EditorUtility.SetDirty(_resizeTarget);
		}

		private void MoveSelectedElement(Rect previewRect, Vector2 mousePosition)
		{
			var mouseWorld = GuiToWorld(previewRect, mousePosition);
			var worldDelta = mouseWorld - _moveStartMouseWorld;
			var parentDelta = WorldDeltaToAnchored(_moveTarget, worldDelta);
			_moveTarget.anchoredPosition = _moveStartAnchoredPosition + parentDelta;
			_snapGuideX = null;
			_snapGuideY = null;

			GetWorldBounds(_moveTarget, out var left, out var right, out var bottom, out var top);
			GetSnapLines(_moveTarget, out var xLines, out var yLines);
			float snapThreshold = GetWorldUnitsPerPixel(previewRect) * 7f;
			float correctionX = FindBestSnapCorrection(new[] { left, right }, xLines, snapThreshold, out var snappedX);
			float correctionY = FindBestSnapCorrection(new[] { bottom, top }, yLines, snapThreshold, out var snappedY);

			if (Mathf.Abs(correctionX) > 0f)
			{
				_moveTarget.anchoredPosition += WorldDeltaToAnchored(_moveTarget, new Vector3(correctionX, 0f, 0f));
				_snapGuideX = snappedX;
			}

			if (Mathf.Abs(correctionY) > 0f)
			{
				_moveTarget.anchoredPosition += WorldDeltaToAnchored(_moveTarget, new Vector3(0f, correctionY, 0f));
				_snapGuideY = snappedY;
			}

			EditorUtility.SetDirty(_moveTarget);
		}

		private void SnapResizeMouse(Rect previewRect, RectTransform target, ref Vector3 mouseWorld)
		{
			GetSnapLines(target, out var xLines, out var yLines);
			float snapThreshold = GetWorldUnitsPerPixel(previewRect) * 7f;
			bool changesX = _resizeHandleIndex is 0 or 1 or 2 or 3 or 4 or 6;
			bool changesY = _resizeHandleIndex is 0 or 1 or 2 or 3 or 5 or 7;
			_snapGuideX = null;
			_snapGuideY = null;

			var handleWorld = mouseWorld - _resizeMouseOffsetWorld;

			if (changesX && TryFindNearestLine(handleWorld.x, xLines, snapThreshold, out var snappedX))
			{
				handleWorld.x = snappedX;
				_snapGuideX = snappedX;
			}

			if (changesY && TryFindNearestLine(handleWorld.y, yLines, snapThreshold, out var snappedY))
			{
				handleWorld.y = snappedY;
				_snapGuideY = snappedY;
			}

			mouseWorld = handleWorld + _resizeMouseOffsetWorld;
		}

		private void GetSnapLines(RectTransform target, out List<float> xLines, out List<float> yLines)
		{
			xLines = new List<float>();
			yLines = new List<float>();

			foreach (var other in _previewObject.GetComponentsInChildren<RectTransform>(true))
			{
				if (other == target || other.gameObject.activeInHierarchy == false || other.IsChildOf(target))
					continue;

				GetWorldBounds(other, out var left, out var right, out var bottom, out var top);
				xLines.Add(left);
				xLines.Add(right);
				yLines.Add(bottom);
				yLines.Add(top);
			}
		}

		private static float FindBestSnapCorrection(float[] ownLines, List<float> targetLines, float threshold, out float snappedLine)
		{
			float bestCorrection = 0f;
			float bestDistance = threshold;
			snappedLine = 0f;

			foreach (var ownLine in ownLines)
			{
				foreach (var targetLine in targetLines)
				{
					float correction = targetLine - ownLine;
					float distance = Mathf.Abs(correction);

					if (distance >= bestDistance)
						continue;

					bestDistance = distance;
					bestCorrection = correction;
					snappedLine = targetLine;
				}
			}

			return bestCorrection;
		}

		private static bool TryFindNearestLine(float value, List<float> lines, float threshold, out float snappedValue)
		{
			float bestDistance = threshold;
			snappedValue = value;
			bool found = false;

			foreach (var line in lines)
			{
				float distance = Mathf.Abs(line - value);

				if (distance >= bestDistance)
					continue;

				bestDistance = distance;
				snappedValue = line;
				found = true;
			}

			return found;
		}

		private static Vector2 WorldDeltaToAnchored(RectTransform target, Vector3 worldDelta)
		{
			var parentDelta = target.parent == null ? worldDelta : target.parent.InverseTransformVector(worldDelta);
			return new Vector2(parentDelta.x, parentDelta.y);
		}

		private static void GetWorldBounds(RectTransform rectTransform, out float left, out float right, out float bottom, out float top)
		{
			var corners = new Vector3[4];
			rectTransform.GetWorldCorners(corners);
			left = right = corners[0].x;
			bottom = top = corners[0].y;

			for (int i = 1; i < corners.Length; i++)
			{
				left = Mathf.Min(left, corners[i].x);
				right = Mathf.Max(right, corners[i].x);
				bottom = Mathf.Min(bottom, corners[i].y);
				top = Mathf.Max(top, corners[i].y);
			}
		}

		private float GetWorldUnitsPerPixel(Rect previewRect)
		{
			return _previewUtility.camera.orthographicSize * 2f / Mathf.Max(1f, previewRect.height);
		}

		private static Vector3[] GetHandleWorldPositions(Vector3[] corners)
		{
			return new[]
			{
				corners[0],
				corners[1],
				corners[2],
				corners[3],
				(corners[0] + corners[1]) * 0.5f,
				(corners[1] + corners[2]) * 0.5f,
				(corners[2] + corners[3]) * 0.5f,
				(corners[3] + corners[0]) * 0.5f,
			};
		}

		private static MouseCursor GetHandleCursor(int handleIndex)
		{
			return handleIndex switch
			{
				0 or 2 => MouseCursor.ResizeUpLeft,
				1 or 3 => MouseCursor.ResizeUpRight,
				4 or 6 => MouseCursor.ResizeHorizontal,
				5 or 7 => MouseCursor.ResizeVertical,
				_ => MouseCursor.Arrow,
			};
		}

		private Vector3 GuiToWorld(Rect previewRect, Vector2 guiPosition)
		{
			var camera = _previewUtility.camera;
			float worldHeight = camera.orthographicSize * 2f;
			float worldWidth = worldHeight * previewRect.width / Mathf.Max(1f, previewRect.height);
			float worldX = camera.transform.position.x + (guiPosition.x - previewRect.center.x) / Mathf.Max(1f, previewRect.width) * worldWidth;
			float worldY = camera.transform.position.y - (guiPosition.y - previewRect.center.y) / Mathf.Max(1f, previewRect.height) * worldHeight;
			return new Vector3(worldX, worldY, 0f);
		}

		private Vector2 WorldToGui(Rect previewRect, Vector3 worldPosition)
		{
			var camera = _previewUtility.camera;
			float worldHeight = camera.orthographicSize * 2f;
			float worldWidth = worldHeight * previewRect.width / Mathf.Max(1f, previewRect.height);
			float guiX = previewRect.center.x + (worldPosition.x - camera.transform.position.x) / worldWidth * previewRect.width;
			float guiY = previewRect.center.y - (worldPosition.y - camera.transform.position.y) / worldHeight * previewRect.height;
			return new Vector2(guiX, guiY);
		}

		private static Rect GetHandleRect(Vector2 center)
		{
			const float handleSize = 9f;
			return new Rect(center.x - handleSize * 0.5f, center.y - handleSize * 0.5f, handleSize, handleSize);
		}

		private void EnsureRootAtOrigin()
		{
			if (_previewObject == null)
				return;

			_previewObject.transform.position = Vector3.zero;
			_previewObject.transform.localPosition = Vector3.zero;

			if (_previewObject.TryGetComponent(out RectTransform rectTransform))
				rectTransform.anchoredPosition3D = Vector3.zero;
		}
	}
}
