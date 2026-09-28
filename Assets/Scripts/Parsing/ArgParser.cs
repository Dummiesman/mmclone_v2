using System;
using System.Collections.Generic;

public class ArgParser 
{
    public class Argument
    {
        public string Name;
        public List<string> Parameters = new List<string>();
    }

    private static List<string> switches = new List<string>();
    private static Dictionary<string, Argument> args = new Dictionary<string, Argument>();

    public static Argument Get(string name)
    {
        Argument ret = null;
        args.TryGetValue(name.ToLowerInvariant(), out ret);
        return ret;
    }

    public static bool GetFlag(string name)
    {
        return Get(name) != null;
    }

    public static void Reset()
    {
        args.Clear();
        switches.Clear();
    }

    public static void Parse(string[] cmdArgs)
    {
        string lastArg = null;
        for (int i = 0; i < cmdArgs.Length; i++)
        {
            string argOrParam = cmdArgs[i];
            if (argOrParam.Length == 0)
                continue;

            if (argOrParam[0] == '/')
            {
                // this is a switch
                switches.Add(argOrParam.Substring(1));
                lastArg = null;
            }
            else if (argOrParam[0] == '-')
            {
                // this is an argument
                string argName = argOrParam.Substring(1);
                string argKey = argName.ToLowerInvariant();
                lastArg = argKey;

                // add key if missing
                if (!args.ContainsKey(argKey))
                    args.Add(argKey, new Argument() { Name = argName });
            }
            else if (!string.IsNullOrEmpty(lastArg))
            {
                // this is a parameter
                args[lastArg].Parameters.Add(argOrParam);
            }
        }
    }
	public static void Parse()
    {
        Parse(Environment.GetCommandLineArgs());
    }
}
