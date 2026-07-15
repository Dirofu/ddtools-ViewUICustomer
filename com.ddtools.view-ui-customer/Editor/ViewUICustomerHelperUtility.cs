using System;
using System.Collections.Generic;
using System.Linq;
using Core.Scripts.UI.Universal.ViewUICustomer;
using UnityEditor;
using UnityEngine;

namespace Core.EditorTools.ViewUICustomer
{
	internal static class ViewUICustomerHelperUtility
	{
		public static Type ResolveHelperType(Type viewType, Type elementType)
		{
			var candidates = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
				.Where(type => type.IsAbstract == false && GetHelperElementType(type) == elementType)
				.ToArray();

			if (candidates.Length == 0)
				return null;

			string expectedName = viewType.Name.EndsWith("View", StringComparison.Ordinal)
				? $"{viewType.Name.Substring(0, viewType.Name.Length - "View".Length)}GetHelper"
				: $"{viewType.Name}GetHelper";

			return candidates.FirstOrDefault(type => type.Namespace == viewType.Namespace && type.Name == expectedName)
				?? candidates.FirstOrDefault(type => type.Namespace == viewType.Namespace)
				?? candidates[0];
		}

		public static Component GetHelper(GameObject gameObject, Type helperType)
		{
			return gameObject == null || helperType == null ? null : gameObject.GetComponent(helperType);
		}

		public static string GetElementTypeName(Component helper)
		{
			var property = GetTypeProperty(helper);

			if (property == null)
				return "?";

			var enumType = GetHelperElementType(helper.GetType());
			return Enum.GetName(enumType, property.intValue) ?? property.intValue.ToString();
		}

		public static int GetElementTypeValue(Component helper)
		{
			return GetTypeProperty(helper)?.intValue ?? 0;
		}

		public static void SetElementType(Component helper, int value)
		{
			if (helper == null)
				return;

			Undo.RecordObject(helper, "Change View UI element type");
			var serializedObject = new SerializedObject(helper);
			var property = serializedObject.FindProperty("_type");

			if (property == null)
				return;

			property.intValue = value;
			serializedObject.ApplyModifiedProperties();
			EditorUtility.SetDirty(helper);
		}

		public static int? FindFirstUnusedValue(GameObject root, Type helperType, Type elementType)
		{
			var usedValues = new HashSet<int>();

			foreach (var helper in root.GetComponentsInChildren(helperType, true).OfType<Component>())
				usedValues.Add(GetElementTypeValue(helper));

			foreach (var value in Enum.GetValues(elementType).Cast<object>().Select(Convert.ToInt32))
			{
				if (value >= 0 && usedValues.Contains(value) == false)
					return value;
			}

			return null;
		}

		public static bool NeedsHelper(GameObject gameObject)
		{
			foreach (var component in gameObject.GetComponents<Component>())
			{
				if (component == null)
					continue;

				var type = component.GetType();

				while (type != null)
				{
					if (type.FullName is "UnityEngine.UI.Image" or "UnityEngine.UI.RawImage" or "UnityEngine.UI.Text" or "TMPro.TMP_Text")
						return true;

					type = type.BaseType;
				}
			}

			return false;
		}

		public static Dictionary<int, int> GetValueUsage(GameObject root, Type helperType)
		{
			var usage = new Dictionary<int, int>();

			if (root == null || helperType == null)
				return usage;

			foreach (var helper in root.GetComponentsInChildren(helperType, true).OfType<Component>())
			{
				int value = GetElementTypeValue(helper);
				usage[value] = usage.TryGetValue(value, out var count) ? count + 1 : 1;
			}

			return usage;
		}

		public static bool SyncHelpersToSource(
			GameObject previewRoot,
			GameObject sourceRoot,
			Type helperType,
			out string error)
		{
			error = null;

			if (previewRoot == null || sourceRoot == null || helperType == null)
				return true;

			try
			{
				if (EditorUtility.IsPersistent(sourceRoot))
					return SyncPrefabAsset(previewRoot, sourceRoot, helperType, out error);

				SyncDirect(previewRoot, sourceRoot, helperType);
				PrefabUtility.RecordPrefabInstancePropertyModifications(sourceRoot);
				return true;
			}
			catch (Exception exception)
			{
				error = $"Не удалось синхронизировать GetHelper: {exception.Message}";
				return false;
			}
		}

		private static bool SyncPrefabAsset(GameObject previewRoot, GameObject sourceRoot, Type helperType, out string error)
		{
			error = null;
			string prefabPath = AssetDatabase.GetAssetPath(sourceRoot);

			if (string.IsNullOrEmpty(prefabPath))
			{
				error = "Не удалось определить путь исходного prefab asset для сохранения GetHelper.";
				return false;
			}

			var assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			string sourcePath = sourceRoot == assetRoot
				? string.Empty
				: AnimationUtility.CalculateTransformPath(sourceRoot.transform, assetRoot.transform);
			var loadedRoot = PrefabUtility.LoadPrefabContents(prefabPath);

			try
			{
				var loadedSource = string.IsNullOrEmpty(sourcePath) ? loadedRoot : loadedRoot.transform.Find(sourcePath)?.gameObject;

				if (loadedSource == null)
				{
					error = "Не удалось найти выбранный root внутри загруженного prefab asset.";
					return false;
				}

				SyncDirect(previewRoot, loadedSource, helperType);
				PrefabUtility.SaveAsPrefabAsset(loadedRoot, prefabPath);
				return true;
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(loadedRoot);
			}
		}

		internal static void SyncDirect(GameObject previewRoot, GameObject sourceRoot, Type helperType)
		{
			var previewHelpers = previewRoot.GetComponentsInChildren(helperType, true)
				.OfType<Component>()
				.GroupBy(helper => AnimationUtility.CalculateTransformPath(helper.transform, previewRoot.transform))
				.ToDictionary(group => group.Key, group => GetElementTypeValue(group.First()));

			foreach (var sourceHelper in sourceRoot.GetComponentsInChildren(helperType, true).OfType<Component>().ToArray())
			{
				string path = AnimationUtility.CalculateTransformPath(sourceHelper.transform, sourceRoot.transform);

				if (previewHelpers.ContainsKey(path) == false)
					Undo.DestroyObjectImmediate(sourceHelper);
			}

			foreach (var pair in previewHelpers)
			{
				var targetTransform = string.IsNullOrEmpty(pair.Key) ? sourceRoot.transform : sourceRoot.transform.Find(pair.Key);

				if (targetTransform == null)
					continue;

				var helper = targetTransform.GetComponent(helperType) ?? Undo.AddComponent(targetTransform.gameObject, helperType);
				SetElementType(helper, pair.Value);
			}
		}

		internal static void SyncRootRectSize(GameObject previewRoot, GameObject sourceRoot)
		{
			if (previewRoot == null || sourceRoot == null)
				return;

			if (previewRoot.transform is not RectTransform previewRect || sourceRoot.transform is not RectTransform sourceRect)
				return;

			Undo.RecordObject(sourceRect, "Resize View UI Customer root");
			sourceRect.sizeDelta = previewRect.sizeDelta;
			sourceRect.localPosition = Vector3.zero;
			EditorUtility.SetDirty(sourceRect);
			PrefabUtility.RecordPrefabInstancePropertyModifications(sourceRect);
		}

		private static SerializedProperty GetTypeProperty(Component helper)
		{
			return helper == null ? null : new SerializedObject(helper).FindProperty("_type");
		}

		private static Type GetHelperElementType(Type helperType)
		{
			for (var type = helperType; type != null; type = type.BaseType)
			{
				if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ViewUIGetHelper<>))
					return type.GetGenericArguments()[0];
			}

			return null;
		}
	}
}
