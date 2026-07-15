using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	[CreateAssetMenu(fileName = "ViewUICustomerPreset", menuName = "DDTools/UI/View UI Customer Preset")]
	public class ViewUICustomerPreset : ScriptableObject
	{
		[SerializeField] private ViewUICustomerPresetData _data = new();

		public ViewUICustomerPresetData Data
		{
			get => _data;
			set => _data = value;
		}
	}

	[Serializable]
	public class ViewUICustomerPresetData
	{
		public string ViewTypeName;
		public string ElementTypeName;
		public List<ViewUICustomerStateData> States = new();
	}

	[Serializable]
	public class ViewUICustomerStateData
	{
		public string ViewType;
		public List<ViewUICustomerGroupData> Groups = new();
	}

	[Serializable]
	public class ViewUICustomerGroupData
	{
		public int StateType;
		public List<ViewUICustomerElementData> Elements = new();
	}

	[Serializable]
	public class ViewUICustomerElementData
	{
		public string ElementType;
		public string ElementPath;
		public bool IsActive;
		public Color Color;
		public string MaterialGuid;
		public Vector3 LocalPosition;
		public bool UsesAnchoredPosition;
		public Vector2 SizeDelta;
	}
}
