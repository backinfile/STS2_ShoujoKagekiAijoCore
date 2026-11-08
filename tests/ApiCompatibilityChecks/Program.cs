using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 3) throw new ArgumentException("Pass old API, current API and native game data directory.");
var previous = Read(args[0], args[2]);
var current = Read(args[1], args[2]);
var missing = previous.Except(current).Order().ToArray();
if (missing.Length != 0) throw new InvalidOperationException("API compatibility changes:\n" + string.Join("\n", missing));
Console.WriteLine($"API V1 compatibility passed: {previous.Count} published type/member signatures preserved.");

static HashSet<string> Read(string file, string dependencies)
{
    var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
    context.Resolving += (loader, name) =>
    {
        string path = Path.Combine(dependencies, name.Name + ".dll");
        return File.Exists(path) ? loader.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
    };
    try
    {
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(file));
        if (assembly.GetName().Version != new Version(1, 0, 0, 0)) throw new InvalidOperationException("API assembly version changed.");
        var signatures = new HashSet<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetExportedTypes())
        {
            string owner = type.FullName!;
            signatures.Add($"{owner}:type:{type.IsInterface}:{type.IsEnum}:{type.IsValueType}:{type.IsSealed}");
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                string result = method is MethodInfo info ? Name(info.ReturnType) : "new";
                string parameters = string.Join(",", method.GetParameters().Select(p => Name(p.ParameterType) +
                    (p.HasDefaultValue ? "=" + p.DefaultValue : "")));
                signatures.Add($"{owner}:{method.IsStatic}:{method.Name}<{(method is MethodInfo generic ? generic.GetGenericArguments().Length : 0)}>({parameters}):{result}");
            }
            foreach (var field in type.GetFields(flags))
                signatures.Add($"{owner}:field:{field.Name}:{Name(field.FieldType)}:{field.IsStatic}:{field.IsInitOnly}:" +
                    (field.IsLiteral ? field.GetRawConstantValue() : ""));
        }
        return signatures;
    }
    finally { context.Unload(); }
}
static string Name(Type type) => type.IsGenericType
    ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(Name)) + ">"
    : type.FullName ?? type.Name;
