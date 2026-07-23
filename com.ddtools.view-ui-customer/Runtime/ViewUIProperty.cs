using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	internal enum ViewUIPropertyValueType
	{
		Integer,
		Boolean,
		Float,
		String,
		Color,
		ObjectReference,
		Vector2,
		Vector3,
		Vector4,
		Rect,
		AnimationCurve,
		Bounds,
		Quaternion,
		Vector2Int,
		Vector3Int,
		RectInt,
		BoundsInt,
		Gradient,
		Hash128,
	}

	[Serializable]
	internal sealed class ViewUIPropertyValue
	{
		public long Integer;
		public bool Boolean;
		public double Float;
		public string String;
		public Color Color;
		public Vector4 Vector;
		public Rect Rect;
		public AnimationCurve Curve;
		public Bounds Bounds;
		public Quaternion Quaternion;
		public Vector3Int VectorInt;
		public RectInt RectInt;
		public BoundsInt BoundsInt;
		public Gradient Gradient;
		public Hash128 Hash128;
	}

	/// <summary>
	/// A serialized component property captured by the editor. At runtime it becomes active only
	/// when its value differs between visual states of the same view/element pair.
	/// </summary>
	[Serializable]
	internal sealed class ViewUIProperty
	{
		[SerializeField] private string _componentType;
		[SerializeField] private int _componentIndex;
		[SerializeField] private string _propertyPath;
		[SerializeField] private ViewUIPropertyValueType _valueType;
		[SerializeField] private string _valueJson;
		[SerializeField] private UnityEngine.Object _objectValue;

		[NonSerialized] private bool _isUsed;
		[NonSerialized] private Component _component;
		[NonSerialized] private ViewUIPropertyValue _value;
		[NonSerialized] private object _startValue;
		[NonSerialized] private bool _didLogApplyFailure;

		internal string Key => $"{_componentType}:{_componentIndex}:{_propertyPath}";
		internal string SerializedValue => _valueJson;
		internal UnityEngine.Object ObjectValue => _objectValue;

		internal bool HasSameValue(ViewUIProperty other)
		{
			return other != null
				&& _valueType == other._valueType
				&& string.Equals(_valueJson, other._valueJson, StringComparison.Ordinal)
				&& _objectValue == other._objectValue;
		}

		internal void SetUsed(bool isUsed)
		{
			_isUsed = isUsed
				&& IsTextContentProperty() == false
				&& IsLocalizationAliasProperty() == false;
		}

		private bool IsTextContentProperty()
		{
			if (_propertyPath != "m_text")
				return false;

			var componentType = Type.GetType(_componentType);
			return componentType != null && typeof(TMP_Text).IsAssignableFrom(componentType);
		}

		private bool IsLocalizationAliasProperty()
		{
			return _propertyPath == "_alias"
				&& _componentType != null
				&& _componentType.StartsWith("Core.Scripts.Language.LanguageElement,", StringComparison.Ordinal);
		}

		internal bool TryBegin(GameObject element, Action<string> logWarning)
		{
			if (_isUsed == false)
				return false;

			_component = ResolveComponent(element);

			if (_component == null)
			{
				logWarning?.Invoke($"Component [{_componentType}] for property [{_propertyPath}] was not found on [{element.name}].");
				return false;
			}

			_value ??= JsonUtility.FromJson<ViewUIPropertyValue>(_valueJson) ?? new ViewUIPropertyValue();
			if (TryGetValue(_component, _propertyPath, out _startValue))
				return true;

			logWarning?.Invoke($"Property [{_propertyPath}] could not be read from [{_component.GetType().Name}] on [{element.name}].");
			_component = null;
			return false;
		}

		internal void Apply(float progress)
		{
			if (_component == null)
				return;

			var targetValue = GetTargetValue();
			var value = Interpolate(_startValue, targetValue, progress);
			if (TrySetValue(_component, _propertyPath, value))
				NotifyChanged(_component);
			else
				LogApplyFailure();
		}

		internal void Complete()
		{
			if (_component != null)
			{
				if (TrySetValue(_component, _propertyPath, GetTargetValue()))
					NotifyChanged(_component);
				else
					LogApplyFailure();
			}

			_component = null;
			_startValue = null;
		}

		private void LogApplyFailure()
		{
			if (_didLogApplyFailure || _component == null)
				return;

			_didLogApplyFailure = true;
			Debug.LogWarning($"ViewUICustomer: property [{_propertyPath}] could not be written to [{_component.GetType().Name}].", _component);
		}

		private Component ResolveComponent(GameObject element)
		{
			var components = element.GetComponents<Component>();
			int currentIndex = 0;

			foreach (var component in components)
			{
				if (component == null || component.GetType().AssemblyQualifiedName != _componentType)
					continue;

				if (currentIndex++ == _componentIndex)
					return component;
			}

			return null;
		}

		private object GetTargetValue()
		{
			switch (_valueType)
			{
				case ViewUIPropertyValueType.Integer: return _value.Integer;
				case ViewUIPropertyValueType.Boolean: return _value.Boolean;
				case ViewUIPropertyValueType.Float: return _value.Float;
				case ViewUIPropertyValueType.String: return _value.String;
				case ViewUIPropertyValueType.Color: return _value.Color;
				case ViewUIPropertyValueType.ObjectReference: return _objectValue;
				case ViewUIPropertyValueType.Vector2: return new Vector2(_value.Vector.x, _value.Vector.y);
				case ViewUIPropertyValueType.Vector3: return new Vector3(_value.Vector.x, _value.Vector.y, _value.Vector.z);
				case ViewUIPropertyValueType.Vector4: return _value.Vector;
				case ViewUIPropertyValueType.Rect: return _value.Rect;
				case ViewUIPropertyValueType.AnimationCurve: return _value.Curve;
				case ViewUIPropertyValueType.Bounds: return _value.Bounds;
				case ViewUIPropertyValueType.Quaternion: return _value.Quaternion;
				case ViewUIPropertyValueType.Vector2Int: return new Vector2Int(_value.VectorInt.x, _value.VectorInt.y);
				case ViewUIPropertyValueType.Vector3Int: return _value.VectorInt;
				case ViewUIPropertyValueType.RectInt: return _value.RectInt;
				case ViewUIPropertyValueType.BoundsInt: return _value.BoundsInt;
				case ViewUIPropertyValueType.Gradient: return _value.Gradient;
				case ViewUIPropertyValueType.Hash128: return _value.Hash128;
				default: return null;
			}
		}

		private static object Interpolate(object start, object target, float progress)
		{
			if (start is float startFloat && target is double targetDouble)
				return Mathf.LerpUnclamped(startFloat, (float)targetDouble, progress);
			if (start is double startDouble && target is double targetDoubleValue)
				return startDouble + (targetDoubleValue - startDouble) * progress;
			if (start is Color startColor && target is Color targetColor)
				return Color.LerpUnclamped(startColor, targetColor, progress);
			if (start is Vector2 startVector2 && target is Vector2 targetVector2)
				return Vector2.LerpUnclamped(startVector2, targetVector2, progress);
			if (start is Vector3 startVector3 && target is Vector3 targetVector3)
				return Vector3.LerpUnclamped(startVector3, targetVector3, progress);
			if (start is Vector4 startVector4 && target is Vector4 targetVector4)
				return Vector4.LerpUnclamped(startVector4, targetVector4, progress);
			if (start is Quaternion startQuaternion && target is Quaternion targetQuaternion)
				return Quaternion.SlerpUnclamped(startQuaternion, targetQuaternion, progress);

			return progress >= 1f ? target : start;
		}

		private static bool TryGetValue(object target, string propertyPath, out object value)
		{
			if (TryGetNativeValue(target, propertyPath, out value))
				return true;

			value = target;
			var parts = propertyPath.Replace(".Array.data[", "[").Split('.');

			foreach (var part in parts)
			{
				if (TryGetPathPart(value, part, out value) == false)
					return false;
			}

			return true;
		}

		private static bool TryGetPathPart(object target, string part, out object value)
		{
			value = null;

			if (target == null)
				return false;

			int bracketIndex = part.IndexOf('[');
			string fieldName = bracketIndex < 0 ? part : part.Substring(0, bracketIndex);
			var field = FindField(target.GetType(), fieldName);

			if (field == null)
				return false;

			value = field.GetValue(target);

			if (bracketIndex >= 0 && value is IList list)
			{
				int index = int.Parse(part.Substring(bracketIndex + 1, part.Length - bracketIndex - 2));
				if (index < 0 || index >= list.Count)
					return false;
				value = list[index];
			}

			return true;
		}

		private static bool TrySetValue(object target, string propertyPath, object value)
		{
			if (TrySetNativeValue(target, propertyPath, value))
				return true;

			var parts = propertyPath.Replace(".Array.data[", "[").Split('.');
			return TrySetValueRecursive(target, parts, 0, value, out _);
		}

		private static bool TryGetNativeValue(object target, string propertyPath, out object value)
		{
			value = null;

			if (target is TMP_Text text && propertyPath == "m_margin")
			{
				value = text.margin;
				return true;
			}

			if (target is Behaviour behaviour && propertyPath == "m_Enabled")
			{
				value = behaviour.enabled;
				return true;
			}

			if (target is not Transform transform)
				return false;

			switch (propertyPath)
			{
				case "m_LocalPosition": value = transform.localPosition; return true;
				case "m_LocalRotation": value = transform.localRotation; return true;
				case "m_LocalScale": value = transform.localScale; return true;
			}

			if (transform is not RectTransform rectTransform)
				return false;

			switch (propertyPath)
			{
				case "m_AnchorMin": value = rectTransform.anchorMin; return true;
				case "m_AnchorMax": value = rectTransform.anchorMax; return true;
				case "m_AnchoredPosition": value = rectTransform.anchoredPosition; return true;
				case "m_SizeDelta": value = rectTransform.sizeDelta; return true;
				case "m_Pivot": value = rectTransform.pivot; return true;
			}

			return false;
		}

		private static bool TrySetNativeValue(object target, string propertyPath, object value)
		{
			if (target is TMP_Text text && propertyPath == "m_margin" && value is Vector4 margin)
			{
				text.margin = margin;
				return true;
			}

			if (target is Behaviour behaviour && propertyPath == "m_Enabled" && value is bool enabled)
			{
				behaviour.enabled = enabled;
				return true;
			}

			if (target is not Transform transform)
				return false;

			switch (propertyPath)
			{
				case "m_LocalPosition" when value is Vector3 position: transform.localPosition = position; return true;
				case "m_LocalRotation" when value is Quaternion rotation: transform.localRotation = rotation; return true;
				case "m_LocalScale" when value is Vector3 scale: transform.localScale = scale; return true;
			}

			if (transform is not RectTransform rectTransform)
				return false;

			switch (propertyPath)
			{
				case "m_AnchorMin" when value is Vector2 anchorMin: rectTransform.anchorMin = anchorMin; return true;
				case "m_AnchorMax" when value is Vector2 anchorMax: rectTransform.anchorMax = anchorMax; return true;
				case "m_AnchoredPosition" when value is Vector2 anchoredPosition: rectTransform.anchoredPosition = anchoredPosition; return true;
				case "m_SizeDelta" when value is Vector2 sizeDelta: rectTransform.sizeDelta = sizeDelta; return true;
				case "m_Pivot" when value is Vector2 pivot: rectTransform.pivot = pivot; return true;
			}

			return false;
		}

		private static bool TrySetValueRecursive(object target, string[] parts, int partIndex, object value, out object updatedTarget)
		{
			updatedTarget = target;
			if (target == null)
				return false;

			string part = parts[partIndex];
			int bracketIndex = part.IndexOf('[');
			string fieldName = bracketIndex < 0 ? part : part.Substring(0, bracketIndex);
			var field = FindField(target.GetType(), fieldName);

			if (field == null)
				return false;

			if (bracketIndex >= 0)
			{
				if (field.GetValue(target) is not IList list)
					return false;

				int index = int.Parse(part.Substring(bracketIndex + 1, part.Length - bracketIndex - 2));
				if (index < 0 || index >= list.Count)
					return false;

				if (partIndex == parts.Length - 1)
				{
					var itemType = GetListItemType(list, index);
					if (TryConvertValue(value, itemType, out var convertedValue) == false)
						return false;
					list[index] = convertedValue;
				}
				else if (TrySetValueRecursive(list[index], parts, partIndex + 1, value, out var updatedItem))
					list[index] = updatedItem;
				else
					return false;
			}
			else if (partIndex == parts.Length - 1)
			{
				if (TryConvertValue(value, field.FieldType, out var convertedValue) == false)
					return false;
				field.SetValue(target, convertedValue);
			}
			else
			{
				var child = field.GetValue(target);
				if (TrySetValueRecursive(child, parts, partIndex + 1, value, out var updatedChild) == false)
					return false;
				field.SetValue(target, updatedChild);
			}

			updatedTarget = target;
			return true;
		}

		private static Type GetListItemType(IList list, int index)
		{
			var listType = list.GetType();
			if (listType.IsArray)
				return listType.GetElementType();
			if (listType.IsGenericType)
				return listType.GetGenericArguments()[0];
			return list[index]?.GetType();
		}

		private static bool TryConvertValue(object value, Type targetType, out object convertedValue)
		{
			convertedValue = null;
			if (targetType == null)
				return false;

			if (value == null)
				return targetType.IsValueType == false || Nullable.GetUnderlyingType(targetType) != null;

			if (targetType.IsInstanceOfType(value))
			{
				convertedValue = value;
				return true;
			}

			try
			{
				if (targetType.IsEnum)
				{
					convertedValue = Enum.ToObject(targetType, value);
					return true;
				}

				if (targetType == typeof(LayerMask))
				{
					convertedValue = new LayerMask { value = Convert.ToInt32(value) };
					return true;
				}

				if (targetType == typeof(Color32) && value is Color color)
				{
					convertedValue = (Color32)color;
					return true;
				}

				if (targetType == typeof(Color) && value is Color32 color32)
				{
					convertedValue = (Color)color32;
					return true;
				}

				if (targetType == typeof(char))
				{
					convertedValue = Convert.ToChar(value);
					return true;
				}

				if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
				{
					convertedValue = Convert.ChangeType(value, targetType);
					return true;
				}
			}
			catch (Exception)
			{
				return false;
			}

			return false;
		}

		private static FieldInfo FindField(Type type, string fieldName)
		{
			while (type != null)
			{
				var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				if (field != null)
					return field;
				type = type.BaseType;
			}

			return null;
		}

		private static void NotifyChanged(Component component)
		{
			if (component is Graphic graphic)
				graphic.SetAllDirty();

			if (component.transform is RectTransform rectTransform)
				LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
		}

#if UNITY_EDITOR
		internal static bool TryCapture(Component component, int componentIndex, SerializedProperty property, out ViewUIProperty result)
		{
			if (component is TMP_Text && property.propertyPath == "m_text")
			{
				result = null;
				return false;
			}

			if (property.propertyPath == "_alias"
				&& component.GetType().FullName == "Core.Scripts.Language.LanguageElement")
			{
				result = null;
				return false;
			}

			result = new ViewUIProperty
			{
				_componentType = component.GetType().AssemblyQualifiedName,
				_componentIndex = componentIndex,
				_propertyPath = property.propertyPath,
			};

			var value = new ViewUIPropertyValue();

			switch (property.propertyType)
			{
				case SerializedPropertyType.Integer:
				case SerializedPropertyType.Enum:
				case SerializedPropertyType.LayerMask:
				case SerializedPropertyType.Character:
					result._valueType = ViewUIPropertyValueType.Integer;
					value.Integer = property.longValue;
					break;
				case SerializedPropertyType.Boolean:
					result._valueType = ViewUIPropertyValueType.Boolean;
					value.Boolean = property.boolValue;
					break;
				case SerializedPropertyType.Float:
					result._valueType = ViewUIPropertyValueType.Float;
					value.Float = property.doubleValue;
					break;
				case SerializedPropertyType.String:
					result._valueType = ViewUIPropertyValueType.String;
					value.String = property.stringValue;
					break;
				case SerializedPropertyType.Color:
					result._valueType = ViewUIPropertyValueType.Color;
					value.Color = property.colorValue;
					break;
				case SerializedPropertyType.ObjectReference:
					result._valueType = ViewUIPropertyValueType.ObjectReference;
					result._objectValue = property.objectReferenceValue;
					break;
				case SerializedPropertyType.Vector2:
					result._valueType = ViewUIPropertyValueType.Vector2;
					value.Vector = property.vector2Value;
					break;
				case SerializedPropertyType.Vector3:
					result._valueType = ViewUIPropertyValueType.Vector3;
					value.Vector = property.vector3Value;
					break;
				case SerializedPropertyType.Vector4:
					result._valueType = ViewUIPropertyValueType.Vector4;
					value.Vector = property.vector4Value;
					break;
				case SerializedPropertyType.Rect:
					result._valueType = ViewUIPropertyValueType.Rect;
					value.Rect = property.rectValue;
					break;
				case SerializedPropertyType.AnimationCurve:
					result._valueType = ViewUIPropertyValueType.AnimationCurve;
					value.Curve = property.animationCurveValue;
					break;
				case SerializedPropertyType.Bounds:
					result._valueType = ViewUIPropertyValueType.Bounds;
					value.Bounds = property.boundsValue;
					break;
				case SerializedPropertyType.Quaternion:
					result._valueType = ViewUIPropertyValueType.Quaternion;
					value.Quaternion = property.quaternionValue;
					break;
				case SerializedPropertyType.Vector2Int:
					result._valueType = ViewUIPropertyValueType.Vector2Int;
					var vector2Int = property.vector2IntValue;
					value.VectorInt = new Vector3Int(vector2Int.x, vector2Int.y);
					break;
				case SerializedPropertyType.Vector3Int:
					result._valueType = ViewUIPropertyValueType.Vector3Int;
					value.VectorInt = property.vector3IntValue;
					break;
				case SerializedPropertyType.RectInt:
					result._valueType = ViewUIPropertyValueType.RectInt;
					value.RectInt = property.rectIntValue;
					break;
				case SerializedPropertyType.BoundsInt:
					result._valueType = ViewUIPropertyValueType.BoundsInt;
					value.BoundsInt = property.boundsIntValue;
					break;
				case SerializedPropertyType.Gradient:
					result._valueType = ViewUIPropertyValueType.Gradient;
					value.Gradient = property.gradientValue;
					break;
				case SerializedPropertyType.Hash128:
					result._valueType = ViewUIPropertyValueType.Hash128;
					value.Hash128 = property.hash128Value;
					break;
				default:
					result = null;
					return false;
			}

			result._valueJson = JsonUtility.ToJson(value);
			return true;
		}
#endif
	}
}
