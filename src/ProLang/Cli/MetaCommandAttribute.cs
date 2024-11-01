using System.Reflection;

namespace ProLang.Cli
{
    [AttributeUsage(AttributeTargets.Method,AllowMultiple =false)]
    internal class MetaCommandAttribute : Attribute
    {
        public MetaCommandAttribute(string name,string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; set; }

        public string Description { get; set; }
    }

    internal sealed class MetaCommand
    {
        public MetaCommand(string name, string description,MethodInfo method)
        {
            Name = name;
            Description = description;
            Method = method;
        }
        public string Name { get; set; }

        public string Description { get; set; }

        public MethodInfo Method { get; set; }
    }
}
