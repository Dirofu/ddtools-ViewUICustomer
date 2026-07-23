using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	[Serializable]
	public class ViewUIElement<TSlotElementType> 
		where TSlotElementType : Enum
	{
		[SerializeField] private TSlotElementType _slotType;
		[SerializeField] private GameObject _element;
		[SerializeField] private bool _isActive;
		[SerializeField] private Color _color;
		[SerializeField] private Material _material;
		[SerializeField] private Vector3 _rect;
		[SerializeField, HideInInspector] private bool _usesAnchoredPosition;
		[SerializeField] private Vector2 _sizeDelta;
		[SerializeField, HideInInspector] private List<ViewUIProperty> _properties = new();

		private bool _isVisible = true;
		private RectTransform _transitionRectTransform;
		private Image _transitionImage;
		private TMP_Text _transitionText;
		private Vector3 _startPosition;
		private Vector2 _startSizeDelta;
		private Color _startColor;
		private bool _startActive;
		private bool _transitionStarted;
		private bool _useActive;
		private bool _usePosition;
		private bool _useSizeDelta;
		private bool _useColor;
		private bool _useMaterial;

		public TSlotElementType SlotType => _slotType;
		internal IReadOnlyList<ViewUIProperty> Properties => _properties != null
			? _properties
			: Array.Empty<ViewUIProperty>();
		internal bool IsActiveValue => _isActive;
		internal Vector3 PositionValue => _rect;
		internal Vector2 SizeDeltaValue => _sizeDelta;
		internal Color ColorValue => _color;
		internal Material MaterialValue => _material;
		internal bool UsesAnchoredPosition => _usesAnchoredPosition;

		public void UpdateVisibleState(bool newState) => _isVisible = newState;

		public void SaveSettings(GameObject element, TSlotElementType type)
		{
			TrySaveSettings(element, type, null);
		}

		public bool TrySaveSettings(GameObject element, TSlotElementType type, Action<string> logWarning)
		{
			_slotType = type;
			_element = element;

			if (_element == null)
			{
				logWarning?.Invoke($"Element [{type}] has no GameObject reference.");
				return false;
			}

			_isActive = element.activeSelf;

			if (element.TryGetComponent(out RectTransform rectTransform) == true)
			{
				_rect = rectTransform.anchoredPosition3D;
				_usesAnchoredPosition = true;
				_sizeDelta = rectTransform.sizeDelta;
			}
			else
			{
				logWarning?.Invoke($"Element [{type}] on [{element.name}] has no {nameof(RectTransform)}.");
			}

			if (element.TryGetComponent(out Image image))
				SaveSettings(image);
			else if (element.TryGetComponent(out TMP_Text text))
				SaveSettings(text);

#if UNITY_EDITOR
			CaptureProperties(element);
#endif

			return true;
		}

		public void SetSettings()
		{
			TrySetSettings(null);
		}

		public bool TrySetSettings(Action<string> logWarning)
		{
			if (_element == null)
			{
				logWarning?.Invoke($"Element [{_slotType}] has lost GameObject reference.");
				return false;
			}

			if (_useActive && _isActive == false)
			{
				_element.SetActive(false);
				return true;
			}

			if (_useActive)
				_element.SetActive(true);

			if (_element.TryGetComponent(out RectTransform rectTransform) == true)
			{
				if (_usePosition && _usesAnchoredPosition)
					rectTransform.anchoredPosition3D = _rect;
				else if (_usePosition)
					rectTransform.localPosition = _rect;

				if (_useSizeDelta)
					rectTransform.sizeDelta = _sizeDelta;
			}
			else
			{
				logWarning?.Invoke($"Element [{_slotType}] on [{_element.name}] has no {nameof(RectTransform)}.");
			}

			if (_element.TryGetComponent(out Image image))
				SetSettings(image);
			else if (_element.TryGetComponent(out TMP_Text text))
				SetSettings(text);

			foreach (var property in _properties ?? new List<ViewUIProperty>())
			{
				if (property.TryBegin(_element, logWarning))
					property.Complete();
			}

			return true;
		}

		public bool TryBeginTransition(Action<string> logWarning)
		{
			if (_element == null)
			{
				logWarning?.Invoke($"Element [{_slotType}] has lost GameObject reference.");
				return false;
			}

			_startActive = _element.activeSelf;
			_transitionStarted = true;

			if (_useActive && _isActive == true)
				_element.SetActive(true);

			_transitionRectTransform = null;
			_transitionImage = null;
			_transitionText = null;

			if (_element.TryGetComponent(out _transitionRectTransform) == true)
			{
				_startPosition = _usesAnchoredPosition
					? _transitionRectTransform.anchoredPosition3D
					: _transitionRectTransform.localPosition;
				_startSizeDelta = _transitionRectTransform.sizeDelta;
			}
			else
			{
				logWarning?.Invoke($"Element [{_slotType}] on [{_element.name}] has no {nameof(RectTransform)}.");
			}

			if (_element.TryGetComponent(out _transitionImage) == true)
				_startColor = _transitionImage.color;
			else if (_element.TryGetComponent(out _transitionText) == true)
				_startColor = _transitionText.color;

			foreach (var property in _properties ?? new List<ViewUIProperty>())
				property.TryBegin(_element, logWarning);

			return true;
		}

		public void ApplyTransition(float progress)
		{
			if (_element == null)
				return;

			if (_transitionRectTransform != null)
			{
				if (_usePosition && _usesAnchoredPosition)
					_transitionRectTransform.anchoredPosition3D = Vector3.LerpUnclamped(_startPosition, _rect, progress);
				else if (_usePosition)
					_transitionRectTransform.localPosition = Vector3.LerpUnclamped(_startPosition, _rect, progress);

				if (_useSizeDelta)
					_transitionRectTransform.sizeDelta = Vector2.LerpUnclamped(_startSizeDelta, _sizeDelta, progress);
			}

			if (_useColor && _transitionImage != null)
				_transitionImage.color = Color.LerpUnclamped(_startColor, _color, progress);
			else if (_useColor && _transitionText != null)
				_transitionText.color = Color.LerpUnclamped(_startColor, _color, progress);

			foreach (var property in _properties ?? new List<ViewUIProperty>())
				property.Apply(progress);
		}

		public void CompleteTransition()
		{
			if (_element == null)
				return;

			ApplyTransition(1f);

			if (_useMaterial && _transitionImage != null)
				_transitionImage.material = _material;

			foreach (var property in _properties ?? new List<ViewUIProperty>())
				property.Complete();

			if (_useActive)
				_element.SetActive(_isActive);

			_transitionStarted = false;
		}

		public void CancelTransition()
		{
			if (_element == null || _transitionStarted == false)
				return;

			if (_useActive)
				_element.SetActive(_startActive);

			_transitionStarted = false;
		}

		private void SaveSettings(Image image)
		{
			_color = image.color;
			_material = image.material;
		}

		private void SaveSettings(TMP_Text text)
		{
			_color = text.color;
		}

		private void SetSettings(Image image)
		{
			if (_useColor)
				image.color = _color;
			if (_useMaterial)
				image.material = _material;
		}

		private void SetSettings(TMP_Text text)
		{
			if (_useColor)
				text.color = _color;
		}

		internal void ResetUsage()
		{
			_useActive = false;
			_usePosition = false;
			_useSizeDelta = false;
			_useColor = false;
			_useMaterial = false;
			foreach (var property in _properties ?? new List<ViewUIProperty>())
				property.SetUsed(false);
		}

		internal void SetBuiltInUsage(bool active, bool position, bool sizeDelta, bool color, bool material)
		{
			_useActive = active;
			_usePosition = position;
			_useSizeDelta = sizeDelta;
			_useColor = color;
			_useMaterial = material;
		}

		internal void SetPropertyUsage(HashSet<string> usedProperties)
		{
			foreach (var property in _properties ?? new List<ViewUIProperty>())
				property.SetUsed(usedProperties.Contains(property.Key));
		}

#if UNITY_EDITOR
		private void CaptureProperties(GameObject element)
		{
			_properties ??= new List<ViewUIProperty>();
			_properties.Clear();
			var componentTypeCounts = new Dictionary<Type, int>();

			foreach (var component in element.GetComponents<Component>())
			{
				if (component == null || component is ViewUIGetHelper<TSlotElementType> || IsViewUICustomer(component.GetType()))
					continue;

				var componentType = component.GetType();
				componentTypeCounts.TryGetValue(componentType, out int componentIndex);
				componentTypeCounts[componentType] = componentIndex + 1;
				var serializedObject = new UnityEditor.SerializedObject(component);
				var property = serializedObject.GetIterator();
				bool enterChildren = true;

				while (property.NextVisible(enterChildren))
				{
					enterChildren = property.propertyType == UnityEditor.SerializedPropertyType.Generic;

					if (ShouldSkipProperty(component, property.propertyPath))
						continue;

					if (ViewUIProperty.TryCapture(component, componentIndex, property, out var snapshot))
					{
						_properties.Add(snapshot);
						enterChildren = false;
					}
				}
			}
		}

		private static bool IsViewUICustomer(Type type)
		{
			while (type != null)
			{
				if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ViewUICustomer<,>))
					return true;
				type = type.BaseType;
			}

			return false;
		}

		private static bool ShouldSkipProperty(Component component, string propertyPath)
		{
			if (propertyPath == "m_Script" || propertyPath == "m_GameObject")
				return true;

			if (component is TMP_Text && propertyPath == "m_text")
				return true;

			if (component is RectTransform && (propertyPath == "m_AnchoredPosition" || propertyPath == "m_SizeDelta"))
				return true;

			if (component is Graphic && (propertyPath == "m_Color" || propertyPath == "m_Material"))
				return true;

			return false;
		}
#endif
	}
}
