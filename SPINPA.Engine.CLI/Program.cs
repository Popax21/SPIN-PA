﻿using System;
using System.CommandLine;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SPINPA.Engine.CLI;

public static class Program {
    public static async Task Main(string[] args) {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        RootCommand root = new RootCommand("A primitive test CLI interface for the SPIN-PA engine");

        foreach(Type type in Assembly.GetExecutingAssembly().DefinedTypes) {
            if(!type.IsAbstract && type.IsAssignableTo(typeof(CLICommand))) root.AddCommand(((CLICommand) Activator.CreateInstance(type)!).Command);
        }

        await root.InvokeAsync(args);
    }
}