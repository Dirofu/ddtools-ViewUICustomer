using System;
using UnityEngine;
using System.Collections.Generic;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	[Serializable]
	public class ViewUIGroupType<TType, TSlotElementType>
		where TType : Enum
		where TSlotElementType: Enum
	{
		[SerializeField] protected ViewUIStateType _type;
		[SerializeField] protected List<ViewUIElement<TSlotElementType>> _elements = new();

		private bool _isVisible = true;

		private Dictionary<TSlotElementType, ViewUIElement<TSlotElementType>> _elementsDic = new();
		
		public ViewUIStateType Type => _type;
		public ViewUIElement<TSlotElementType> this[TSlotElementType stateType] => _elementsDic[stateType];
		public Dictionary<TSlotElementType, ViewUIElement<TSlotElementType>> Elements => _elementsDic;

		public void UpdateVisibleState(bool newState) => _isVisible = newState;

		public void InitDictionary()
		{
			UpdateElements();
		}

		public void AddNewElement(ViewUIStateType elementType, TSlotElementType type, ViewUIElement<TSlotElementType> newElement, GameObject @object)
		{
			_type = elementType;
			newElement.SaveSettings(@object, type);
			_elements.Add(newElement);
			_elementsDic[type] = newElement;
		}

		internal void SetType(ViewUIStateType type)
		{
			_type = type;
		}

#if UNITY_EDITOR
		internal void EditorNormalizeElements()
		{
			if (_elements == null)
			{
				_elements = new List<ViewUIElement<TSlotElementType>>();
				return;
			}

			var knownTypes = new HashSet<TSlotElementType>();

			for (int i = 0; i < _elements.Count;)
			{
				var element = _elements[i];

				if (element == null || knownTypes.Add(element.SlotType) == false)
					_elements.RemoveAt(i);
				else
					i++;
			}
		}
#endif

		public void RewriteElement(ViewUIStateType elementType, TSlotElementType type, ViewUIElement<TSlotElementType> element, GameObject @object)
		{
			_type = elementType;
			element.SaveSettings(@object, type);
		}

		public void UpdateElements()
		{
			UpdateElements(null, null);
		}

		public bool UpdateElements(Action<string> logWarning, string context)
		{
			_elementsDic.Clear();
			bool isValid = true;

			if (_elements == null)
			{
				logWarning?.Invoke($"{context}: elements list is null.");
				return false;
			}

			for (int i = 0; i < _elements.Count; i++)
			{
				var element = _elements[i];

				if (element == null)
				{
					logWarning?.Invoke($"{context}: element at index {i} is null.");
					isValid = false;
					continue;
				}

				if (_elementsDic.ContainsKey(element.SlotType) == true)
				{
					logWarning?.Invoke($"{context}: duplicate element type [{element.SlotType}] at index {i}. Last value is ignored.");
					isValid = false;
					continue;
				}

				_elementsDic.Add(element.SlotType, element);
			}

			return isValid;
		}

		public bool TryGetElement(TSlotElementType type, out ViewUIElement<TSlotElementType> element)
		{
			return _elementsDic.TryGetValue(type, out element);
		}

		public void Reset()
		{
			_elements.Clear();
			_elementsDic.Clear();
		}
	}
}
