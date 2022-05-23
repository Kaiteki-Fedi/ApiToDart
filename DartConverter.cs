using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using ApiToDart.Dart;

using Humanizer;

using OpenApiBrowser.Model;

namespace ApiToDart
{
    public partial class DartConverter
    {

        private static void AppendConstructor(CodeStringBuilder code, DataSchema schema, string className, Job job)
        {
            var parameters = schema.Properties
                .Select((kv) =>
                {
                    bool nullable = IsNullable(kv.Value, job, className, kv.Key);
                    return GetParameter(kv.Key, nullable);
                })
                .ToArray();

            DartCodeWriter.WriteMultiLineConstructor(code, className, ElementFlags.Const, parameters);
        }

        private static bool IsNullable(Property property, Job job, string className, string propertyName)
        {
            var propertyId = className + '.' + propertyName;

            if (job.Default.NullabilityCorrections?.TryGetValue(propertyId, out var v) == true)
            {
                return v;
            }

            return property.Nullable || property.Optional;
        }

        private static DartConstructorParameter GetParameter(string name, bool nullable)
        {
            return new DartConstructorParameter()
            {
                Name = name.ApplyNaming(DartNamingConvention.Field),
                Required = !nullable,
                This = true,
            };
        }

        private static void AppendDartDocComment(CodeStringBuilder code, string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return;

            // TODO: Separate first sentence and trailing lines, etc.
            var lines = new[] { comment };

            foreach (var line in lines)
            {
                code.AppendLine("/// " + line);
            }
        }

        private static (string Name, DataSchema Schema)? FindSchema(Specification spec, string name, bool ignoreCase = true)
        {
            StringComparison stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            bool predicate(KeyValuePair<string, DataSchema> kv)
            {
                return kv.Key.Equals(name, stringComparison);
            };

            if (spec.Components.Schemas.Any(predicate))
            {
                var kv = spec.Components.Schemas.First(predicate);
                return (kv.Key, kv.Value);
            }
            else
            {
                return null;
            }
        }

        private static void AppendImports(StringBuilder sb, Job job, Specification spec, IEnumerable<Property> properties, string currentSchema)
        {
            var dataTypes = properties.Select(Utils.GetPropertyReference)
                                      .Distinct()
                                      .Where(v => v != null)
                                      .Where(t => t != currentSchema);

            List<string> imports = new() { "package:json_annotation/json_annotation.dart" };

            foreach (var type in dataTypes)
            {
                var schema = FindSchema(spec, type);

                if (schema.HasValue)
                {
                    string name = schema.Value.Name;
                    JobSettings schemaOverride = Utils.GetJobSettings(job, name);

                    string fileName = name.ApplyNaming(DartNamingConvention.FileName);
                    imports.Add("package:" + schemaOverride.ImportPrefix + '/' + fileName + ".dart");
                }
                else
                {
                    Utils.WriteWarning("Couldn't find schema entry");
                    continue;
                }
            }

            foreach (var import in imports)
            {
                sb.AppendLine($"import '{import}';");
            }
        }

        private string ToDartType(Job job, Specification spec, Property property, string name, string className)
        {
            string type = property?.Type;

            if (type == "array")
            {
                return $"Iterable<{ToDartType(job, spec, property.Items, name, className)}>";
            }
            else if (job.Default.TypeCorrections?.TryGetValue(className + '.' + name, out var correctedType) == true)
            {
                return correctedType;
            }

            if (Utils.GetPropertyReference(property) is string schemaReference)
            {
                var schema = FindSchema(spec, schemaReference, false);

                if (!schema.HasValue)
                {
                    throw new Exception($"Couldn't find referenced schema/component: {schemaReference}");
                }

                string schemaType = schema.Value.Schema.Type;

                if (schemaType == "object")
                {
                    var settings = Utils.GetJobSettings(job, schemaReference);
                    return settings.ClassNamePrefix + schemaReference.Pascalize();
                }

                type = schemaType;
            }

            const bool allowAnonymizedObjectGen = false;

            return type switch
            {           
                "string" when property.Format == "date-time" => "DateTime",
                "string" when property.Enum != null => GetEnumName(className, name),
                "string" => "String",
                "boolean" => "bool",
                "number" or "integer" => "int",
                // Use singularized property name as type name when schema with that name exists.
                "object" when name.Singularize() is string sName && FindSchema(spec, sName) != null => sName.ApplyNaming(DartNamingConvention.Class),
                // Generate type name when anonymized object with declared properties is used
                "object" when allowAnonymizedObjectGen && property.Properties != null => GetEnumName(className, name),
                "object" => "Map<String, dynamic>",
                null or "any" => "dynamic",
                _ => throw new ArgumentOutOfRangeException(nameof(property), $"Unknown type {property.Type}, resolved it as {type}"),
            };
        }

        private static string GetEnumName(string className, string propertyName)
        {
            return $"{className}_{propertyName}".Pascalize();
        }

        private void AppendField(CodeStringBuilder code, string jsonKey, Job job, Specification spec, Property property, string className)
        {
            // add dartdoc comment
            AppendDartDocComment(code, property.Description);

            // add json attribute
            code.AppendLine($"@JsonKey(name: '{jsonKey}')");

            string fieldName = jsonKey.ApplyNaming(DartNamingConvention.Field);
            string fieldType = ToDartType(job, spec, property, fieldName, className);
            bool fieldNullability = IsNullable(property, job, className, fieldName);
            string fieldLine = DartCodeWriter.GetField(fieldType, fieldName, fieldNullability, ElementFlags.Final);
            code.AppendLine(fieldLine);
        }

        public string ToDartFile(Job job, Specification spec, DataSchema schema, string schemaName)
        {
            var code = new CodeStringBuilder();
            var jobSettings = Utils.GetJobSettings(job, schemaName);

            // append imports
            AppendImports(code, job, spec, schema.Properties?.Values, schemaName);

            // append part reference
            string fileName = schemaName.ApplyNaming(DartNamingConvention.FileName);
            code.AppendLine($"part '{fileName}.g.dart';");
            code.AppendLine();

            // append description
            AppendDartDocComment(code, schema.Description);

            string className = jobSettings.ClassNamePrefix + schemaName.Pascalize();
            AppendClass(job, code, className, spec, schema);

            foreach (var (name, property) in schema.Properties)
            {
                if (property.Enum != null)
                {
                    string enumName = GetEnumName(className, name);
                    AppendEnum(code, enumName, property.Enum);
                }

                if (property.Type == "object" && property.Properties != null)
                {
                    string schemalessClassName = GetEnumName(className, name);
                }
            }

            return code.ToString();
        }

        public void AppendEnum(CodeStringBuilder code, string enumName, string[] values)
        {
            code.AppendLine($"enum {enumName} {{");

            code.Indent();

            foreach (var value in values)
            {
                code.AppendLine(value + ",");
            }

            code.Unindent();

            code.AppendLine("}");
        }
    
        public void AppendClass(Job job, CodeStringBuilder code, string className, Specification spec, DataSchema schema)
        {
            code.AppendLine("@JsonSerializable()");
            code.AppendLine($"class {className} {{");

            code.Indent();

            // write fields
            foreach (var (name, property) in schema.Properties)
            {
                AppendField(code, name, job, spec, property, className);
            }

            // append constructor
            AppendConstructor(code, schema, className, job);

            // append json
            code.AppendLine();
            code.AppendLine($"factory {className}.fromJson(Map<String, dynamic> json) => _${className}FromJson(json);");
            code.AppendLine($"Map<String, dynamic> toJson() => _${className}ToJson(this);");

            code.Unindent();

            code.AppendLine("}");
        }
    }
}