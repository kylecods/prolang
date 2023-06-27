using System.Text;

namespace ProLang
{
    internal class Program
    {
        static void Main(string[] args)
        {
           using var result = new Source("\\Documents\\code.txt");

            var currentChar = result.CurrentChar();

            var stringBuilder = new StringBuilder();

            while (currentChar != Source.EOF){
                stringBuilder.Append(currentChar);
                currentChar = result.NextChar();
            }

            Console.WriteLine(stringBuilder.ToString());
            
        }
    }
}