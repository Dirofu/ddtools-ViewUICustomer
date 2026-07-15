using System;
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

		private bool _isVisible = true;
		private RectTransform _transitionRectTransform;
		private Image _transitionImage;
		private TMP_Text _transitionText;
		private Vector3 _startPosition;
		private Vector2 _startSizeDelta;
		private Color _startColor;

		public TSlotElementType SlotType => _slotType;

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

			if (_isActive == false)
			{
				_element.SetActive(false);
				return true;
			}

			_element.SetActive(true);

			if (_element.TryGetComponent(out RectTransform rectTransform) == true)
			{
				if (_usesAnchoredPosition)
					rectTransform.anchoredPosition3D = _rect;
				else
					rectTransform.localPosition = _rect;

				if (_sizeDelta != Vector2.zero)
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

			return true;
		}

		public bool TryBeginTransition(Action<string> logWarning)
		{
			if (_element == null)
			{
				logWarning?.Invoke($"Element [{_slotType}] has lost GameObject reference.");
				return false;
			}

			if (_isActive == true)
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

			return true;
		}

		public void ApplyTransition(float progress)
		{
			if (_element == null)
				return;

			if (_transitionRectTransform != null)
			{
				if (_usesAnchoredPosition)
					_transitionRectTransform.anchoredPosition3D = Vector3.LerpUnclamped(_startPosition, _rect, progress);
				else
					_transitionRectTransform.localPosition = Vector3.LerpUnclamped(_startPosition, _rect, progress);

				if (_sizeDelta != Vector2.zero)
					_transitionRectTransform.sizeDelta = Vector2.LerpUnclamped(_startSizeDelta, _sizeDelta, progress);
			}

			if (_transitionImage != null)
				_transitionImage.color = Color.LerpUnclamped(_startColor, _color, progress);
			else if (_transitionText != null)
				_transitionText.color = Color.LerpUnclamped(_startColor, _color, progress);
		}

		public void CompleteTransition()
		{
			if (_element == null)
				return;

			ApplyTransition(1f);

			if (_transitionImage != null)
				_transitionImage.material = _material;

			_element.SetActive(_isActive);
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
			image.color = _color;
			image.material = _material;
		}

		private void SetSettings(TMP_Text text)
		{
			text.color = _color;
		}
	}
}
