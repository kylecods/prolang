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
        public MetaCommand(string name, string description, Action<string[]> handler)
        {
            Name = name;
            Description = description;
            Handler = handler;
        }
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// Runs the command with the arguments the user typed after its name.
        /// </summary>
        /// <remarks>
        /// A delegate rather than a <see cref="MethodInfo"/>: discovering commands by reflecting
        /// over the type's own methods does not survive Native AOT, where reflection metadata is
        /// trimmed. The attribute stays as documentation on each handler; registration is now
        /// explicit in <see cref="Repl.InitializeMetaCommands"/>.
        /// </remarks>
        public Action<string[]> Handler { get; set; }
    }
}
