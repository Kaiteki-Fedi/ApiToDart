using System.Text;

namespace ApiToDart
{
    public class CodeStringBuilder
    {
        public int IndentLevel { get; private set; }

        public string IndentString { get; set; } = "  ";

        private readonly StringBuilder stringBuilder = new();
        private string _buffer = string.Empty;

        public void Append(string value) => _buffer += value;
        public void AppendLine() => stringBuilder.AppendLine();

        public void AppendLine(string value)
        {
            _buffer += value;
            
            AppendIndent();
            stringBuilder.AppendLine(_buffer);

            _buffer = string.Empty;
        }

        private void AppendIndent()
        {
            for (int i = 0; i < IndentLevel; i++)
            {
                stringBuilder.Append(IndentString);
            }
        }

        public void Indent() => IndentLevel++;
        public void Unindent() => IndentLevel--;

        public override string ToString() => stringBuilder.ToString();

        public static implicit operator StringBuilder(CodeStringBuilder sb) => sb.stringBuilder;
        public static implicit operator string(CodeStringBuilder sb) => sb.ToString();
    }
}
