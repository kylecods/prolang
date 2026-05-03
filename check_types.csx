// Check where System.String is defined
var stringType = typeof(string);
var mathType = typeof(System.Math);
var listType = typeof(System.Collections.Generic.List<>);
var dictType = typeof(System.Collections.Generic.Dictionary<,>);

Console.WriteLine($"System.String: {stringType.Assembly.Location}");
Console.WriteLine($"System.Math: {mathType.Assembly.Location}");
Console.WriteLine($"List<>: {listType.Assembly.Location}");
Console.WriteLine($"Dictionary<,>: {dictType.Assembly.Location}");
