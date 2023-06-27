using System.Text;
using ProLang.FrontEnd;

namespace ProLang
{
    internal class Program
    {
        static void Main()
        {
           using var result = new Source(@"C:\Users\Kyle.rotich\Documents\code.txt");

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