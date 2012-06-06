// TypeWriterMethods.cs
// 
// Copyright (c) 2012 Mike Strobel
// 
// This source code is subject to the terms of the Microsoft Reciprocal License (Ms-RL).
// For details, see <http://www.opensource.org/licenses/ms-rl.html>.
// 
// All other rights reserved.

using System;
using System.IO;
using System.Reflection;

namespace StrobelStack.Text.Common
{
    internal static class TypeWriterMethods
    {
         internal static MethodInfo GetWriteMethod(Type valueType)
         {
             return typeof(TextWriter).GetMethod(
                 "Write",
                 BindingFlags.Public | BindingFlags.Instance,
                 Type.DefaultBinder,
                 new[] { valueType },
                 null);
         }
    }
}