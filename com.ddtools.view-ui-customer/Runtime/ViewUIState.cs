using System;
using UnityEngine;
using System.Collections.Generic;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	[Serializable]
	public class ViewUIState<TType, TSlotElementType>
		where TType : Enum
		where TSlotElementType : Enum
	{
		[SerializeField] private TType _stateType;
		[SerializeField] private List<ViewUIGroupType<TType, TSlotElementType>> _groups = new();

		private bool _isVisible = true;

		private Dictionary<ViewUIStateType, ViewUIGroupType<TType, TSlotElementType>> _groupDic = new();

		public TType StateType => _stateType;
		public IReadOnlyDictionary<ViewUIStateType, ViewUIGroupType<TType, TSlotElementType>> Groups => _groupDic;

		public ViewUIGroupType<TType, TSlotElementType> this[ViewUIStateType type] => _groupDic[type];

		public event Action<ViewUIStateType> StateChanged = delegate { };

		public void UpdateVisibleState(bool newState) => _isVisible = newState;

		public void InitDictionary()
		{
			UpdateGroups();
		}

		public void UpdateGroups()
		{
			UpdateGroups(null, null);
		}

		public bool UpdateGroups(Action<string> logWarning, string context)
		{
			_groupDic.Clear();
			bool isValid = true;

			if (_groups == null)
			{
				logWarning?.Invoke($"{context}: groups list is null.");
				return false;
			}

			for (int i = 0; i < _groups.Count; i++)
			{
				var group = _groups[i];

				if (group == null)
				{
					logWarning?.Invoke($"{context}: group at index {i} is null.");
					isValid = false;
					continue;
				}

				if (_groupDic.ContainsKey(group.Type) == true)
				{
					logWarning?.Invoke($"{context}: duplicate state type [{group.Type}] at index {i}. Last value is ignored.");
					isValid = false;
					continue;
				}

				_groupDic.Add(group.Type, group);
			}

			return isValid;
		}

		public void AddNewGroup(ViewUIStateType elementType, TType type, ViewUIGroupType<TType, TSlotElementType> newGroup)
		{
			_stateType = type;
			newGroup.SetType(elementType);
			_groups.Add(newGroup);
			_groupDic[elementType] = newGroup;
		}

#if UNITY_EDITOR
		internal void EditorNormalizeGroups()
		{
			if (_groups == null)
			{
				_groups = new List<ViewUIGroupType<TType, TSlotElementType>>();
				return;
			}

			var knownTypes = new HashSet<ViewUIStateType>();

			for (int i = 0; i < _groups.Count;)
			{
				var group = _groups[i];

				if (group == null || knownTypes.Add(group.Type) == false)
				{
					_groups.RemoveAt(i);
					continue;
				}

				group.EditorNormalizeElements();
				i++;
			}
		}
#endif

		public bool TryGetGroup(ViewUIStateType type, out ViewUIGroupType<TType, TSlotElementType> group)
		{
			return _groupDic.TryGetValue(type, out group);
		}

		public void Reset()
		{
			_groups.Clear();
			_groupDic.Clear();
		}
	}
}
