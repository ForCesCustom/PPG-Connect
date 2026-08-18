using System;
using System.IO;
using System.Reflection;

internal static class ReflectionProbe
{
    private static string directory;

    private static void Main(string[] args)
    {
        if (args == null || args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ReflectionProbe <assembly-path> <type-name-fragment>");
            Environment.Exit(2);
        }

        directory = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += Resolve;
        Assembly assembly = Assembly.ReflectionOnlyLoadFrom(args[0]);
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { types = error.Types; }

        string fragment = args[1];
        bool findFields = fragment.StartsWith("field:", StringComparison.OrdinalIgnoreCase);
        if (findFields) fragment = fragment.Substring("field:".Length);
        bool exact = fragment.Length > 1 && fragment[0] == '=';
        if (exact) fragment = fragment.Substring(1);
        foreach (Type type in types)
        {
            if (type == null || type.FullName == null) continue;
            if (findFields)
            {
                bool matched = false;
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        string fieldType = field.FieldType.FullName ?? field.FieldType.Name;
                        if (fieldType.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (!matched) { Console.WriteLine("TYPE " + type.FullName); matched = true; }
                            Console.WriteLine("  FIELD " + fieldType + " " + field.Name);
                        }
                    }
                    catch (Exception) { }
                }
                continue;
            }
            if (exact ? !string.Equals(type.FullName, fragment, StringComparison.Ordinal) : type.FullName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine("TYPE " + type.FullName);
            try
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    Console.WriteLine("  METHOD " + method.ReturnType.Name + " " + method.Name + "(" + ParameterList(method) + ")");
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    Console.WriteLine("  PROPERTY " + property.PropertyType.Name + " " + property.Name);
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    Console.WriteLine("  FIELD " + field.FieldType.Name + " " + field.Name);
            }
            catch (Exception error) { Console.WriteLine("  METADATA ERROR " + error.GetType().Name); }
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name + ".dll";
        string candidate = Path.Combine(directory, simpleName);
        return File.Exists(candidate) ? Assembly.ReflectionOnlyLoadFrom(candidate) : null;
    }

    private static string ParameterList(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string value = string.Empty;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) value += ", ";
            value += parameters[i].ParameterType.Name + " " + parameters[i].Name;
        }
        return value;
    }
}
