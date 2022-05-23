using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ApiToDart.Dart;

using Humanizer;

using OpenApiBrowser.Model;

namespace ApiToDart
{
    public partial class DartConverter
    {
        public Job Job { get; }
        public JobSettings Settings => Job.Default;
        public (string Name, DataSchema Schema)[] Schemas { get; }

        public DartConverter(Job job, Specification specification)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            Schemas = ResolveSchemas(specification.Components.Schemas);
        }

        private static (string, DataSchema)[] ResolveSchemas(Dictionary<string, DataSchema> schemas)
        {
            int length = schemas.Count;
            var array = new (string, DataSchema)[length];

            for (int i = 0; i < length; i++)
            {
                var key = schemas.Keys.ElementAt(i);
                DataSchema schema = schemas[key];

                if (IsPartial(schema))
                {
                    schema = ResolveSchema(schemas, schema);
                }

                array[i] = (key, schema);
            }

            return array;

        }

        private static bool IsPartial(DataSchema schema)
        {
            return schema.OneOf?.Count > 0 ||
                    schema.AllOf?.Count > 0 ||
                    schema.AnyOf?.Count > 0;
        }

        private static DataSchema ResolveSchema(Dictionary<string, DataSchema> schemas, DataSchema schema)
        {
            if (schema.Properties?.Count > 0)
            {
                throw new NotImplementedException("Unsupported situation, mixed schemas with properties defined");
            }

            var partialReferences = new List<DataSchema>();

            if (schema.AllOf != null)
            {
                partialReferences.AddRange(schema.AllOf);
            }
            if (schema.OneOf != null)
            {
                partialReferences.AddRange(schema.OneOf);
            }
            if (schema.AnyOf != null)
            {
                partialReferences.AddRange(schema.AnyOf);
            }

            DataSchema mergedSchema = schema with
            {
                OneOf = null,
                AllOf = null,
                AnyOf = null,
            };

            foreach (var item in partialReferences)
            {
                DataSchema referencedSchema = item;

                // Resolve reference
                if (referencedSchema.Ref != null)
                {
                    referencedSchema = schemas[referencedSchema.Ref];
                }

                // Check whether our referenced schema references another one
                // If so, recurse.
                if (IsPartial(referencedSchema))
                {
                    referencedSchema = ResolveSchema(schemas, referencedSchema);
                }

                if (!(referencedSchema.Properties?.Count > 0))
                {
                    Utils.WriteWarning($"[{schema.Title}] Tried to merge a schema with another one who doesn't have any properties.");
                    continue;
                }

                // Merge
                var properties = mergedSchema.Properties ?? new Dictionary<string, Property>();
                var newProperties = referencedSchema.Properties.Where(kv => !properties.ContainsKey(kv.Key));
                mergedSchema = mergedSchema with
                {
                    Properties = new(properties.Concat(newProperties))
                };
            }

            return mergedSchema;
        }

        private void AppendConstructor(CodeStringBuilder code, DataSchema schema, string className)
        {
            var parameters = schema.Properties
                .Select((kv) =>
                {
                    bool nullable = IsNullable(kv.Value, className, kv.Key);
                    return GetParameter(kv.Key, nullable);
                })
                .ToArray();

            DartCodeWriter.WriteMultiLineConstructor(code, className, ElementFlags.Const, parameters);
        }

        private bool IsNullable(Property property, string className, string propertyName)
        {
            var propertyId = className + '.' + propertyName;

            if (Settings.NullabilityCorrections?.TryGetValue(propertyId, out var v) == true)
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

        private (string Name, DataSchema Schema)? FindSchema(string name, bool ignoreCase = true)
        {
            StringComparison stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            bool predicate((string, DataSchema) kv)
            {
                return kv.Item1.Equals(name, stringComparison);
            };

            return Schemas.FirstOrDefault(predicate);
        }

        private void AppendImports(StringBuilder sb, IEnumerable<Property> properties, string currentSchema)
        {
            var dataTypes = properties.Select(Utils.GetPropertyReference)
                                      .Distinct()
                                      .Where(v => v != null)
                                      .Where(t => t != currentSchema);

            List<string> imports = new() { "package:json_annotation/json_annotation.dart" };

            foreach (var type in dataTypes)
            {
                var schema = FindSchema(type);

                if (schema.HasValue)
                {
                    string name = schema.Value.Name;
                    JobSettings schemaOverride = Utils.GetJobSettings(Job, name);

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

        private string ToDartType(Property property, string name, string className)
        {
            string type = property?.Type;

            // Check for array
            if (type == "array")
            {
                return $"Iterable<{ToDartType(property.Items, name, className)}>";
            }

            // Check if manually corrected type was given.
            var memberId = className + '.' + name;
            if (Settings.TypeCorrections?.TryGetValue(memberId, out var correctedType) == true)
            {
                return correctedType;
            }

            if (Utils.GetPropertyReference(property) is string schemaReference)
            {
                var schema = FindSchema(schemaReference, false);

                if (!schema.HasValue)
                {
                    throw new Exception($"Couldn't find referenced schema/component: {schemaReference}");
                }

                string schemaType = schema.Value.Schema.Type;

                if (schemaType == "object")
                {
                    var settings = Utils.GetJobSettings(Job, schemaReference);
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
                "object" when name.Singularize() is string sName && FindSchema(sName) != null => sName.ApplyNaming(DartNamingConvention.Class),
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

        private void AppendField(CodeStringBuilder code, string jsonKey, Property property, string className)
        {
            // add dartdoc comment
            AppendDartDocComment(code, property.Description);

            string name = jsonKey.ApplyNaming(DartNamingConvention.Field);

            // add json attribute
            if (jsonKey != name)
            {
                code.AppendLine($"@JsonKey(name: '{jsonKey}')");
            }

            string type = ToDartType(property, name, className);
            bool nullability = IsNullable(property, className, name);
            string line = DartCodeWriter.GetField(type, name, nullability, ElementFlags.Final);
            code.AppendLine(line);
        }

        public string ToDartFile(DataSchema schema, string schemaName)
        {
            var code = new CodeStringBuilder();
            var jobSettings = Utils.GetJobSettings(Job, schemaName);

            // append imports
            AppendImports(code, schema.Properties?.Values, schemaName);

            // append part reference
            string fileName = schemaName.ApplyNaming(DartNamingConvention.FileName);
            code.AppendLine($"part '{fileName}.g.dart';");
            code.AppendLine();

            // append description
            AppendDartDocComment(code, schema.Description);

            string className = jobSettings.ClassNamePrefix + schemaName.Pascalize();
            AppendClass(code, className, schema);

            foreach (var (name, property) in schema.Properties)
            {
                if (property.Enum != null)
                {
                    string enumName = GetEnumName(className, name);
                    AppendEnum(code, enumName, property.Enum);
                }

                // if (property.Type == "object" && property.Properties != null)
                // {
                //     string schemalessClassName = GetEnumName(className, name);
                // }
            }

            return code.ToString();
        }

        public static void AppendEnum(CodeStringBuilder code, string enumName, string[] values)
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
    
        public void AppendClass(CodeStringBuilder code, string className, DataSchema schema)
        {
            code.AppendLine("@JsonSerializable()");
            code.AppendLine($"class {className} {{");

            code.Indent();

            // write fields
            foreach (var (name, property) in schema.Properties)
            {
                AppendField(code, name, property, className);
            }

            // append constructor
            AppendConstructor(code, schema, className);

            // append json
            code.AppendLine();
            code.AppendLine($"factory {className}.fromJson(Map<String, dynamic> json) => _${className}FromJson(json);");
            code.AppendLine($"Map<String, dynamic> toJson() => _${className}ToJson(this);");

            code.Unindent();

            code.AppendLine("}");
        }
    }
}