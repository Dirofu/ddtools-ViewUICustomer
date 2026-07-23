using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	public class ViewUICustomer<TType, TSlotElementType> : MonoBehaviour
#if UNITY_EDITOR
		, IViewUICustomerEditor
#endif
		where TType : Enum
		where TSlotElementType : Enum
	{
#if UNITY_EDITOR
		private bool CaptureCurrentState(TType statusType, ViewUIStateType stateType)
		{
			NormalizeEditorStates();

			if (_states == null)
				_states = new List<ViewUIState<TType, TSlotElementType>>();

			RebuildStatesDictionary();

			if (_statesDic.ContainsKey(statusType) == false)
			{
				ViewUIState<TType, TSlotElementType> newState = new();
				_statesDic.Add(statusType, newState);
			}

			this[statusType].UpdateGroups();

			if (this[statusType].Groups.ContainsKey(stateType) == false)
			{
				ViewUIGroupType<TType, TSlotElementType> newGroup = new();
				this[statusType].AddNewGroup(stateType, statusType, newGroup);
			}

			foreach (var item in this[statusType].Groups)
				item.Value.UpdateElements();

			ViewUIGetHelper<TSlotElementType>[] getHelpers = gameObject.GetComponentsInChildren<ViewUIGetHelper<TSlotElementType>>(true);
			bool isValid = true;

			foreach (var helper in getHelpers)
			{
				if (this[statusType][stateType].Elements.TryGetValue(helper.Type, out ViewUIElement<TSlotElementType> element) == false)
				{
					ViewUIElement<TSlotElementType> newElement = new();
					this[statusType][stateType].AddNewElement(stateType, helper.Type, newElement, helper.gameObject);
				}
				else
				{
					this[statusType][stateType].RewriteElement(stateType, helper.Type, element, helper.gameObject);
				}

				if (this[statusType][stateType].Elements[helper.Type].TrySaveSettings(helper.gameObject, helper.Type, null) == false)
					isValid = false;
			}

			_states = new List<ViewUIState<TType, TSlotElementType>>(_statesDic.Values);
			return isValid;
		}

		Type IViewUICustomerEditor.EditorViewType => typeof(TType);
		Type IViewUICustomerEditor.EditorElementType => typeof(TSlotElementType);

		bool IViewUICustomerEditor.EditorCaptureState(Enum viewType, ViewUIStateType stateType)
		{
			if (!(viewType is TType typedViewType))
				return false;

			return CaptureCurrentState(typedViewType, stateType);
		}

		bool IViewUICustomerEditor.EditorApplyState(Enum viewType, ViewUIStateType stateType)
		{
			if (!(viewType is TType typedViewType))
				return false;

			NormalizeEditorStates();
			UpdateStates();
			return TrySetState(stateType, typedViewType);
		}

		bool IViewUICustomerEditor.EditorBeginStateTransition(Enum viewType, ViewUIStateType stateType)
		{
			if (!(viewType is TType typedViewType))
				return false;

			NormalizeEditorStates();
			UpdateStates();
			return TrySetStateInternal(stateType, typedViewType, true);
		}

		float IViewUICustomerEditor.EditorTransitionDuration => _transitionDuration;

		float IViewUICustomerEditor.EditorEvaluateTransition(float normalizedTime)
		{
			return _transitionCurve == null ? normalizedTime : _transitionCurve.Evaluate(normalizedTime);
		}

		void IViewUICustomerEditor.EditorApplyTransition(float progress)
		{
			ApplyTransition(progress);
		}

		void IViewUICustomerEditor.EditorCompleteTransition()
		{
			CompleteTransition();
		}

		bool IViewUICustomerEditor.EditorHasState(Enum viewType, ViewUIStateType stateType)
		{
			if (!(viewType is TType typedViewType))
				return false;

			NormalizeEditorStates();
			UpdateStates();

			if (_statesDic.TryGetValue(typedViewType, out var state) == false)
				return false;
			return state.TryGetGroup(stateType, out _);
		}

		bool IViewUICustomerEditor.EditorValidateCapture(out string error)
		{
			var helpers = gameObject.GetComponentsInChildren<ViewUIGetHelper<TSlotElementType>>(true);

			if (helpers.Length == 0)
			{
				error = $"Не найдены компоненты {nameof(ViewUIGetHelper<TSlotElementType>)}. Добавьте сгенерированный GetHelper на каждый UI-объект, состояние которого нужно сохранять.";
				return false;
			}

			var helperObjects = new Dictionary<TSlotElementType, GameObject>();

			foreach (var helper in helpers)
			{
				if (helperObjects.TryGetValue(helper.Type, out var existingObject))
				{
					error = $"Тип элемента [{helper.Type}] используется несколько раз: [{existingObject.name}] и [{helper.gameObject.name}]. Каждый GetHelper должен иметь уникальный ElementType.";
					return false;
				}

				helperObjects.Add(helper.Type, helper.gameObject);
			}

			error = null;
			return true;
		}

		bool IViewUICustomerEditor.EditorRefreshStates()
		{
			NormalizeEditorStates();
			return UpdateStates();
		}

		private void NormalizeEditorStates()
		{
			if (_states == null)
			{
				_states = new List<ViewUIState<TType, TSlotElementType>>();
				return;
			}

			var knownTypes = new HashSet<TType>();

			for (int i = 0; i < _states.Count;)
			{
				var state = _states[i];

				if (state == null || knownTypes.Add(state.StateType) == false)
				{
					_states.RemoveAt(i);
					continue;
				}

				state.EditorNormalizeGroups();
				i++;
			}
		}

#endif

		[SerializeField] private List<ViewUIState<TType, TSlotElementType>> _states;
		[SerializeField] private bool _buttonSupport = false;
		[SerializeField] private ViewUIButton _button;
		[SerializeField] private ViewUIButtonClickBehavior _clickBehavior = ViewUIButtonClickBehavior.StayActive;
		[SerializeField, Min(0f)] private float _transitionDuration = 0.15f;
		[SerializeField] private AnimationCurve _transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		private TType _currentType;
		private ViewUIStateType _currentStateType = ViewUIStateType.Idle;
		private Coroutine _transitionCoroutine;
		private List<ViewUIElement<TSlotElementType>> _transitionElements = new();
		private bool _isCompletingTransition;

		private Dictionary<TType, ViewUIState<TType, TSlotElementType>> _statesDic = new();
		public ViewUIState<TType, TSlotElementType> this[TType type] => _statesDic[type];
		public TType Type => _currentType;
		public ViewUIStateType StateType => _currentStateType;

		public event Action<TType> StateChanged = delegate { };
		public event Action<ViewUIStateType, TType> VisualStateChanged = delegate { };

		private void Awake()
		{
			UpdateStates();
		}

		private void OnEnable()
		{
			SubscribeButton(true);
		}

		private void OnDisable()
		{
			CompleteTransition();
			SubscribeButton(false);
		}

		/// <summary>
		/// Use this for set view state.<br/><br/><paramref name="stateType"/> setted idle, highlight, active etc. On base use <paramref name="ViewUIStateType"/>. <br /><paramref name="type"/> setted global type like a Ally\Enemy.
		/// </summary>
		/// <param name="stateType">Slot state type</param>
		/// <param name="type">Slot element type</param>
		public void SetState(ViewUIStateType stateType, TType type)
		{
			TrySetState(stateType, type);
		}

		public bool TrySetState(ViewUIStateType stateType, TType type)
		{
			return TrySetStateInternal(stateType, type, false);
		}

		private bool TrySetStateInternal(ViewUIStateType stateType, TType type, bool deferEditorCompletion)
		{
			if (_statesDic.Count == 0 && UpdateStates() == false)
				return false;

			if (_statesDic.TryGetValue(type, out var state) == false)
			{
				LogStateWarning($"View type [{type}] is not configured. Requested state: [{stateType}].");
				return false;
			}

			if (state.TryGetGroup(stateType, out var group) == false)
			{
				LogStateWarning($"Visual state [{stateType}] is not configured for view type [{type}].");
				return false;
			}

			_currentType = type;
			_currentStateType = stateType;

			CancelTransition();

			bool isValid = true;
			_transitionElements.Clear();

			foreach (var element in group.Elements.Values)
			{
				if (element.TryBeginTransition(LogStateWarning) == false)
					isValid = false;
				else
					_transitionElements.Add(element);
			}

			if (deferEditorCompletion)
			{
				if (_transitionDuration <= 0f)
					CompleteTransition();
			}
			else if (Application.isPlaying == false || isActiveAndEnabled == false || _transitionDuration <= 0f)
				CompleteTransition();
			else
				_transitionCoroutine = StartCoroutine(AnimateTransition());

			StateChanged.Invoke(type);
			VisualStateChanged.Invoke(stateType, type);
			return isValid;
		}

		private IEnumerator AnimateTransition()
		{
			float elapsed = 0f;

			while (elapsed < _transitionDuration)
			{
				float normalizedTime = Mathf.Clamp01(elapsed / _transitionDuration);
				float progress = _transitionCurve == null
					? normalizedTime
					: _transitionCurve.Evaluate(normalizedTime);

				ApplyTransition(progress);
				elapsed += Time.unscaledDeltaTime;
				yield return null;
			}

			_transitionCoroutine = null;
			CompleteTransition();
		}

		private void ApplyTransition(float progress)
		{
			foreach (var element in _transitionElements)
				element.ApplyTransition(progress);
		}

		private void CancelTransition()
		{
			if (_transitionCoroutine != null)
			{
				StopCoroutine(_transitionCoroutine);
				_transitionCoroutine = null;
			}

			foreach (var element in _transitionElements)
				element.CancelTransition();

			_transitionElements.Clear();
		}

		private void CompleteTransition()
		{
			if (_isCompletingTransition == true || _transitionElements.Count == 0)
				return;

			_isCompletingTransition = true;

			if (_transitionCoroutine != null)
			{
				StopCoroutine(_transitionCoroutine);
				_transitionCoroutine = null;
			}

			foreach (var element in _transitionElements)
				element.CompleteTransition();

			_transitionElements.Clear();
			_isCompletingTransition = false;
		}

		private bool UpdateStates()
		{
			_statesDic.Clear();
			bool isValid = RebuildStatesDictionary();

			foreach (var statePair in _statesDic)
			{
				var type = statePair.Key;
				var state = statePair.Value;

				if (state.UpdateGroups(LogStateWarning, $"View type [{type}]") == false)
					isValid = false;
				
				foreach (var groupPair in state.Groups)
				{
					if (groupPair.Value.UpdateElements(LogStateWarning, $"View type [{type}], visual state [{groupPair.Key}]") == false)
						isValid = false;
				}
			}

			ConfigurePropertyUsage(_statesDic.Values);

			return isValid;
		}

		private static void ConfigurePropertyUsage(IEnumerable<ViewUIState<TType, TSlotElementType>> states)
		{
			var elementsBySlot = new Dictionary<TSlotElementType, List<ViewUIElement<TSlotElementType>>>();

			foreach (var state in states)
			{
				foreach (var group in state.Groups.Values)
				{
					foreach (var pair in group.Elements)
					{
						if (elementsBySlot.TryGetValue(pair.Key, out var elements) == false)
						{
							elements = new List<ViewUIElement<TSlotElementType>>();
							elementsBySlot.Add(pair.Key, elements);
						}

						elements.Add(pair.Value);
					}
				}
			}

			foreach (var elements in elementsBySlot.Values)
				ConfigureElementUsage(elements);
		}

		private static void ConfigureElementUsage(List<ViewUIElement<TSlotElementType>> elements)
		{
			if (elements.Count == 0)
				return;

			foreach (var element in elements)
				element.ResetUsage();

			bool hasSingleState = elements.Count == 1;
			var first = elements[0];
			bool useActive = hasSingleState;
			bool usePosition = hasSingleState;
			bool useSizeDelta = hasSingleState;
			bool useColor = hasSingleState;
			bool useMaterial = hasSingleState;

			for (int i = 1; i < elements.Count; i++)
			{
				var element = elements[i];
				useActive |= element.IsActiveValue != first.IsActiveValue;
				usePosition |= element.PositionValue != first.PositionValue
					|| element.UsesAnchoredPosition != first.UsesAnchoredPosition;
				useSizeDelta |= element.SizeDeltaValue != first.SizeDeltaValue;
				useColor |= element.ColorValue != first.ColorValue;
				useMaterial |= element.MaterialValue != first.MaterialValue;
			}

			var propertyValues = new Dictionary<string, List<ViewUIProperty>>();
			foreach (var element in elements)
			{
				foreach (var property in element.Properties)
				{
					if (propertyValues.TryGetValue(property.Key, out var values) == false)
					{
						values = new List<ViewUIProperty>();
						propertyValues.Add(property.Key, values);
					}

					values.Add(property);
				}
			}

			var usedProperties = new HashSet<string>();
			foreach (var pair in propertyValues)
			{
				var values = pair.Value;
				bool isUsed = hasSingleState || values.Count != elements.Count;

				for (int i = 1; isUsed == false && i < values.Count; i++)
					isUsed = values[i].HasSameValue(values[0]) == false;

				if (isUsed)
					usedProperties.Add(pair.Key);
			}

			foreach (var element in elements)
			{
				element.SetBuiltInUsage(useActive, usePosition, useSizeDelta, useColor, useMaterial);
				element.SetPropertyUsage(usedProperties);
			}
		}

		private bool RebuildStatesDictionary()
		{
			_statesDic.Clear();
			bool isValid = true;

			if (_states == null)
			{
				LogStateWarning("States list is null.");
				return false;
			}

			for (int i = 0; i < _states.Count; i++)
			{
				var state = _states[i];

				if (state == null)
				{
					LogStateWarning($"State at index {i} is null.");
					isValid = false;
					continue;
				}

				if (_statesDic.ContainsKey(state.StateType) == true)
				{
					LogStateWarning($"Duplicate view type [{state.StateType}] at index {i}. Last value is ignored.");
					isValid = false;
					continue;
				}

				_statesDic.Add(state.StateType, state);
			}

			return isValid;
		}

		private void LogStateWarning(string message)
		{
			Debug.LogWarning($"ViewUICustomer: {message}", this);
		}

		protected virtual void SubscribeButton(bool subscribe)
		{
			if (_buttonSupport == false || _button == null)
				return;

			if (subscribe == true)
			{
				_button.ButtonClicked += OnButtonClicked;
				_button.ButtonPointerUp += OnButtonPointerUp;
				_button.ButtonPointerDown += OnButtonPointerDown;
				_button.ButtonPointerExit += OnButtonPointerExit;
				_button.ButtonPointerEnter += OnButtonPointerEnter;
			}
			else
			{
				_button.ButtonClicked -= OnButtonClicked;
				_button.ButtonPointerUp -= OnButtonPointerUp;
				_button.ButtonPointerDown -= OnButtonPointerDown;
				_button.ButtonPointerExit -= OnButtonPointerExit;
				_button.ButtonPointerEnter -= OnButtonPointerEnter;
			}
		}

		private void OnButtonPointerExit()
		{
			if (_currentStateType == ViewUIStateType.Active)
				return;

			SetState(ViewUIStateType.Idle, _currentType);
		}

		private void OnButtonPointerEnter()
		{
			if (_currentStateType == ViewUIStateType.Active)
				return;

			SetState(ViewUIStateType.Highlight, _currentType);
		}

		private void OnButtonPointerUp()
		{
			if (_currentStateType == ViewUIStateType.Active)
				return;

			SetState(ViewUIStateType.Idle, _currentType);
		}

		private void OnButtonPointerDown()
		{
			if (_currentStateType == ViewUIStateType.Active)
				return;

			SetState(ViewUIStateType.Pressed, _currentType);
		}

		private void OnButtonClicked()
		{
			if (_clickBehavior == ViewUIButtonClickBehavior.StayActive)
			{
				SetState(ViewUIStateType.Active, _currentType);
				return;
			}

			if (_currentStateType != ViewUIStateType.Idle)
				SetState(ViewUIStateType.Idle, _currentType);
		}
	}
}
