﻿namespace SIM.Client
{
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Reflection;
  using System.Security.Principal;
  using CommandLine;
  using Newtonsoft.Json;
  using Sitecore.Diagnostics.Base;
  using JetBrains.Annotations;
  using log4net.Config;
  using log4net.Core;
  using log4net.Layout;
  using log4net.Util;
  using Sitecore.Diagnostics.Logging;
  using SIM.Client.Commands;
  using SIM.Client.Serialization;
  using SIM.Core;
  using SIM.Core.Common;
  using SIM.Core.Logging;

  public static class Program
  {
    public static void Main([NotNull] string[] args)
    {
      Assert.ArgumentNotNull(args, nameof(args));

      InitializeLogging();
      
      CoreApp.LogMainInfo();

      Analytics.Start();

      var filteredArgs = args.ToList();
      var query = GetQueryAndFilterArgs(filteredArgs);
      var wait = GetWaitAndFilterArgs(filteredArgs);

      if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
      {
        Console.WriteLine("Current user account is not Administrator. Please re-run using Administrator user account.");

        if (wait)
        {
          Console.ReadKey();
        }

        Environment.Exit(403);
        return;
      }

      var parser = new Parser(with =>
      {
        with.MutuallyExclusive = true;
        with.HelpWriter = Console.Error;
      });

      Assert.IsNotNull(parser, nameof(parser));

      var options = new MainCommandGroup();
      EnsureAutoCompeteForCommands(options);
      ICommand selectedCommand = null;
      if (!parser.ParseArguments(filteredArgs.ToArray(), options, (verb, command) => selectedCommand = (ICommand)command))
      {
        Console.WriteLine("Note, commands provide output when work is done i.e. without any progress indication.");
        Console.WriteLine("\r\n  --query\t   When specified, allows returning only part of any command's output");
        Console.WriteLine("\r\n  --data\t   When specified, allows returning only 'data' part of any command's output");
        Console.WriteLine("\r\n  --wait\t   When specified, waits for keyboard input before terminating");

        if (wait)
        {
          Console.ReadKey();
        }

        Environment.Exit(Parser.DefaultExitCodeFail);
      }

      Assert.IsNotNull(selectedCommand, nameof(selectedCommand));

      var commandResult = selectedCommand.Execute();
      Assert.IsNotNull(commandResult, nameof(commandResult));

      var result = QueryResult(commandResult, query);
      if (result == null)
      {
        return;
      }

      var serializer = new JsonSerializer
      {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
      };

      serializer.Converters.Add(new DirectoryInfoConverter());

      var writer = Console.Out;
      serializer.Serialize(writer, result);

      if (wait)
      {
        Console.ReadKey();
      }
    }

    private static void InitializeLogging()
    {
      var info = new LogFileAppender
      {
        AppendToFile = true,
        File = "$(currentFolder)\\sim.log",
        Layout = new PatternLayout("%4t %d{ABSOLUTE} %-5p %m%n"),
        SecurityContext = new WindowsSecurityContext(),
        Threshold = Level.Info
      };

      var debug = new LogFileAppender
      {
        AppendToFile = true,
        File = "$(currentFolder)\\sim.debug",
        Layout = new PatternLayout("%4t %d{ABSOLUTE} %-5p %m%n"),
        SecurityContext = new WindowsSecurityContext(),
        Threshold = Level.Debug
      };

      CoreApp.InitializeLogging(info, debug);
    }

    private static void EnsureAutoCompeteForCommands(MainCommandGroup options)
    {
      foreach (var propertyInfo in options.GetType().GetProperties())
      {
        if (typeof(ICommand).IsAssignableFrom(propertyInfo.PropertyType))
        {
          var verb = propertyInfo.GetCustomAttributes().OfType<VerbOptionAttribute>().FirstOrDefault();
          if (verb == null)
          {
            continue;
          }

          var command = verb.LongName;
          if (File.Exists(command))
          {
            continue;
          }

          CreateEmptyFileInCurrentDirectory(command);
        }
      }
    }

    private static void CreateEmptyFileInCurrentDirectory(string command)
    {
      try
      {
        File.Create(command).Close();
      }
      catch
      {
        Log.Warn($"Cannot create file: {command}");
      }
    }

    [CanBeNull]
    private static object QueryResult([NotNull] CommandResult result, [CanBeNull] string query)
    {
      Assert.ArgumentNotNull(result, nameof(result));

      if (string.IsNullOrEmpty(query) || !result.Success)
      {
        return result;
      }

      object obj = result;
      foreach (var chunk in query.Split("./".ToCharArray()))
      {
        if (string.IsNullOrEmpty(chunk))
        {
          continue;
        }

        var newObj = null as object;
        var dictionary = obj as IDictionary;
        if (dictionary != null)
        {
          if (dictionary.Contains(chunk))
          {
            newObj = dictionary[chunk];
          }
        }
        else
        {
          var type = obj.GetType();
          var prop = type.GetProperties().FirstOrDefault(x => x.Name.Equals(chunk, StringComparison.OrdinalIgnoreCase));
          if (prop != null)
          {
            newObj = prop.GetValue(obj, null);
          }
        }

        if (newObj == null)
        {
          Console.WriteLine("Cannot find '" + chunk + "' chunk of '" + query + "' query in the object: ");
          Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));

          return null;
        }

        obj = newObj;
      }

      return obj;
    }

    [CanBeNull]
    private static string GetQueryAndFilterArgs([NotNull] List<string> filteredArgs)
    {
      Assert.ArgumentNotNull(filteredArgs, nameof(filteredArgs));

      var query = string.Empty;
      for (var i = 0; i < filteredArgs.Count; i++)
      {
        if (filteredArgs[i] == "--data")
        {
          filteredArgs[i] = "--query";
          filteredArgs.Insert(i + 1, "data");
        }

        if (filteredArgs[i] != "--query")
        {
          continue;
        }

        filteredArgs.RemoveAt(i);

        if (filteredArgs.Count > i)
        {
          query = filteredArgs[i];
          filteredArgs.RemoveAt(i);
        }

        break;
      }

      return query;
    }

    private static bool GetWaitAndFilterArgs([NotNull] List<string> filteredArgs)
    {
      Assert.ArgumentNotNull(filteredArgs, nameof(filteredArgs));

      for (var i = 0; i < filteredArgs.Count; i++)
      {
        if (filteredArgs[i] != "--wait")
        {
          continue;
        }

        filteredArgs.RemoveAt(i);

        return true;
      }

      return false;
    }
  }
}
