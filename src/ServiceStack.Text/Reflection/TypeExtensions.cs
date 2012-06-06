// TypeExtensions.cs
// 
// Copyright (c) 2012 Mike Strobel
// 
// This source code is subject to the terms of the Microsoft Reciprocal License (Ms-RL).
// For details, see <http://www.opensource.org/licenses/ms-rl.html>.
// 
// All other rights reserved.

using System;

namespace StrobelStack.Text.Reflection
{
    internal static class TypeExtensions
    {
        internal static Type GetNonNullableType(this Type type)
        {
            if (IsNullableType(type))
                return type.GetGenericArguments()[0];
            return type;
        }


        internal static bool IsNullableType(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        } 
    }
}