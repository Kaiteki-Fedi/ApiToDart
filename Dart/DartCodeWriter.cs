using ApiToDart.Dart;

using Humanizer;

using static ApiToDart.DartConverter;

namespace ApiToDart
{
    public static class DartCodeWriter
    {
        public static string GetField(string type, string name, bool nullable = false, ElementFlags flags = ElementFlags.None)
        {
            var stringBuilder = new CodeStringBuilder();

            WriteFlags(stringBuilder, flags);
            stringBuilder.Append(type);
            if (nullable)
            {
                stringBuilder.Append("?");
            }

            stringBuilder.AppendLine(' '+name+';');
            return stringBuilder.ToString();
        }

        public static string ApplyNaming(this string input, DartNamingConvention convention)
        {
            var name = input;

            if (name[0] == '_')
                name = name[1..];

            name = convention switch
            {
                DartNamingConvention.FileName => name.Underscore(),
                DartNamingConvention.Field => name.Camelize(),
                DartNamingConvention.Class => name.Pascalize(),
                _ => name,
            };

            return name;
        }

        

        public static void WriteMultiLineConstructor(this CodeStringBuilder code, string className, ElementFlags flags = ElementFlags.None, params DartConstructorParameter[] parameters)
        {
            WriteFlags(code, flags);
            code.AppendLine($"{className}({{");
            code.Indent();
            
            foreach (var parameter in parameters)
            {
                if (parameter.Required) code.Append("required ");
                if (parameter.This) code.Append("this.");

                code.AppendLine(parameter.Name + ',');
            }

            code.Unindent();
            code.AppendLine("});");
        }

        public static void WriteFlags(CodeStringBuilder stringBuilder, ElementFlags flags)
        {
            switch (flags)
            {
                case ElementFlags.Const:
                    stringBuilder.Append("const ");
                    break;
                case ElementFlags.Final:
                    stringBuilder.Append("final ");
                    break;
                case ElementFlags.Late:
                    stringBuilder.Append("late ");
                    break;
            }
        }
    }

    public struct DartConstructorParameter
    {
        public string Name;
        public bool Required;
        public bool This;
    }
}
