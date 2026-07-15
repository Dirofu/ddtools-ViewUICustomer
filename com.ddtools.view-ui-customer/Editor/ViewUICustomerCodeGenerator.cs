using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Core.EditorTools.ViewUICustomer
{
	internal static class ViewUICustomerCodeGenerator
	{
		public static string GetNamespace(string folderPath)
		{
			var normalizedPath = folderPath.Replace('\\', '/').Trim('/');

			if (normalizedPath.StartsWith("Assets/", StringComparison.Ordinal))
				normalizedPath = normalizedPath.Substring("Assets/".Length);

			return string.Join(".", normalizedPath.Split('/').Select(SanitizeIdentifier).Where(part => string.IsNullOrEmpty(part) == false));
		}

		public static bool Generate(string rawName, string folderPath, bool useBaseViewType, out string error)
		{
			error = null;
			var name = SanitizeIdentifier(rawName);

			if (string.IsNullOrEmpty(name))
			{
				error = "Enter a valid component name.";
				return false;
			}

			if (AssetDatabase.IsValidFolder(folderPath) == false)
			{
				error = "Select a folder inside Assets.";
				return false;
			}

			var namespaceName = GetNamespace(folderPath);

			if (string.IsNullOrEmpty(namespaceName))
			{
				error = "The selected folder cannot be converted to a namespace.";
				return false;
			}

			var elementTypeName = $"{name}ElementType";
			var viewTypeName = useBaseViewType ? "ViewUIBaseType" : $"{name}ViewType";
			var files = new Dictionary<string, string>
			{
				[$"{name}View.cs"] = CreateView(namespaceName, name, viewTypeName, elementTypeName),
				[$"{name}GetHelper.cs"] = CreateHelper(namespaceName, name, elementTypeName),
				[$"{elementTypeName}.cs"] = CreateEnum(namespaceName, elementTypeName, true),
			};

			if (useBaseViewType == false)
				files[$"{viewTypeName}.cs"] = CreateEnum(namespaceName, viewTypeName, false);

			var existingFiles = files.Keys
				.Select(fileName => Path.Combine(folderPath, fileName).Replace('\\', '/'))
				.Where(File.Exists)
				.ToArray();

			if (existingFiles.Length > 0)
			{
				error = $"Generation stopped because files already exist:\n{string.Join("\n", existingFiles)}";
				return false;
			}

			foreach (var file in files)
			{
				var path = Path.Combine(folderPath, file.Key).Replace('\\', '/');
				File.WriteAllText(path, file.Value, new UTF8Encoding(false));
			}

			AssetDatabase.Refresh();
			return true;
		}

		public static bool TryAddElementEnumValue(
			Type enumType,
			string objectName,
			out int value,
			out string memberName,
			out string error)
		{
			value = 0;
			memberName = null;
			error = null;
			var scriptPath = FindEnumScriptPath(enumType);

			if (string.IsNullOrEmpty(scriptPath))
			{
				error = $"Не найден исходный файл enum {enumType.Name}.";
				return false;
			}

			var source = File.ReadAllText(scriptPath);
			var enumMatch = Regex.Match(source, $@"\benum\s+{Regex.Escape(enumType.Name)}\b");

			if (enumMatch.Success == false)
			{
				error = $"В файле {scriptPath} не найден enum {enumType.Name}.";
				return false;
			}

			int openBrace = source.IndexOf('{', enumMatch.Index + enumMatch.Length);
			int closeBrace = FindClosingBrace(source, openBrace);

			if (openBrace < 0 || closeBrace < 0)
			{
				error = $"Не удалось определить границы enum {enumType.Name}.";
				return false;
			}

			string enumBody = source.Substring(openBrace + 1, closeBrace - openBrace - 1);
			var knownNames = new HashSet<string>(Enum.GetNames(enumType));

			foreach (Match match in Regex.Matches(enumBody, @"\b[A-Za-z_][A-Za-z0-9_]*\b\s*(?:=|,|$)"))
				knownNames.Add(Regex.Match(match.Value, @"[A-Za-z_][A-Za-z0-9_]*").Value);

			memberName = SanitizeIdentifier(objectName);

			if (string.IsNullOrEmpty(memberName))
				memberName = "Element";

			string baseName = memberName;
			int suffix = 2;

			while (knownNames.Contains(memberName))
				memberName = $"{baseName}_{suffix++}";

			value = Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt32).DefaultIfEmpty(-1).Max() + 1;

			foreach (Match match in Regex.Matches(enumBody, @"=\s*(-?\d+)"))
			{
				if (int.TryParse(match.Groups[1].Value, out var parsedValue))
					value = Math.Max(value, parsedValue + 1);
			}

			string newline = source.Contains("\r\n") ? "\r\n" : "\n";
			string indentation = DetectMemberIndentation(enumBody);
			string memberSource = $"{indentation}{memberName} = {value},{newline}";
			int insertionIndex = source.LastIndexOf('\n', closeBrace) + 1;
			source = source.Insert(insertionIndex, memberSource);
			File.WriteAllText(scriptPath, source, new UTF8Encoding(false));
			return true;
		}

		private static string CreateView(string namespaceName, string name, string viewTypeName, string elementTypeName)
		{
			return $@"using Core.Scripts.UI.Universal.ViewUICustomer;

namespace {namespaceName}
{{
	public class {name}View : ViewUICustomer<{viewTypeName}, {elementTypeName}>
	{{
	}}
}}
";
		}

		private static string CreateHelper(string namespaceName, string name, string elementTypeName)
		{
			return $@"using Core.Scripts.UI.Universal.ViewUICustomer;

namespace {namespaceName}
{{
	public class {name}GetHelper : ViewUIGetHelper<{elementTypeName}>
	{{
	}}
}}
";
		}

		private static string CreateEnum(string namespaceName, string typeName, bool isElementType)
		{
			string firstMember = isElementType ? "None = -1" : "Default = 0";

			return $@"namespace {namespaceName}
{{
	public enum {typeName}
	{{
		{firstMember},
	}}
}}
";
		}

		private static string FindEnumScriptPath(Type enumType)
		{
			var candidates = AssetDatabase.FindAssets($"{enumType.Name} t:MonoScript")
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => Path.GetExtension(path) == ".cs")
				.OrderByDescending(path => Path.GetFileNameWithoutExtension(path) == enumType.Name);

			foreach (var path in candidates)
			{
				if (Regex.IsMatch(File.ReadAllText(path), $@"\benum\s+{Regex.Escape(enumType.Name)}\b"))
					return path;
			}

			return null;
		}

		private static int FindClosingBrace(string source, int openBrace)
		{
			if (openBrace < 0)
				return -1;

			int depth = 0;

			for (int i = openBrace; i < source.Length; i++)
			{
				if (source[i] == '{')
					depth++;
				else if (source[i] == '}' && --depth == 0)
					return i;
			}

			return -1;
		}

		private static string DetectMemberIndentation(string enumBody)
		{
			var match = Regex.Match(enumBody, @"(?:^|\r?\n)([\t ]+)[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Multiline);
			return match.Success ? match.Groups[1].Value : "\t\t";
		}

		private static string SanitizeIdentifier(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			var builder = new StringBuilder();

			foreach (var symbol in value.Trim())
			{
				if (char.IsLetterOrDigit(symbol) || symbol == '_')
					builder.Append(symbol);
			}

			if (builder.Length == 0)
				return string.Empty;

			if (char.IsDigit(builder[0]))
				builder.Insert(0, '_');

			return builder.ToString();
		}
	}
}
