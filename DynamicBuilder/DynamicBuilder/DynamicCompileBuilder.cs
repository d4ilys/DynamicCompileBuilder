﻿using FreeSql.DataAnnotations;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using FreeSql.Internal.Model;
using System.Text;

namespace FreeSql.Internal
{
    public class DynamicCompileBuilder
    {
        private string _className = string.Empty;
        private TableAttribute _tableAttribute = null;
        private List<DynamicPropertyInfo> _properties = new List<DynamicPropertyInfo>();

        /// <summary>
        /// 配置Class
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="tableAttribute">类标记的特性[Table(Name = "xxx")]</param>
        /// <returns></returns>
        public DynamicCompileBuilder SetClass(string className, TableAttribute tableAttribute)
        {
            _className = className;
            _tableAttribute = tableAttribute;
            return this;
        }

        /// <summary>
        /// 配置属性
        /// </summary>
        /// <param name="propertyName">属性名称</param>
        /// <param name="propertyType">属性类型</param>
        /// <param name="attributes">属性标记的特性-支持多个</param>
        /// <returns></returns>
        public DynamicCompileBuilder Property(string propertyName, Type propertyType, params Attribute[] attributes)
        {
            _properties.Add(new DynamicPropertyInfo()
            {
                PropertyName = propertyName,
                PropertyType = propertyType,
                Attributes = attributes
            });
            return this;
        }

        private void SetTableAttribute(ref TypeBuilder typeBuilder)
        {
            var classCtorInfo = typeof(TableAttribute).GetConstructor(new Type[] { });
            var propertyInfos = typeof(TableAttribute).GetProperties().Where(p => p.CanWrite == true).ToArray();
            if (_tableAttribute == null)
            {
                return;
            }

            var propertyValues = new ArrayList();
            foreach (var propertyInfo in _tableAttribute.GetType().GetProperties().Where(p => p.CanWrite == true))
            {
                propertyValues.Add(propertyInfo.GetValue(_tableAttribute));
            }

            var customAttributeBuilder =
                new CustomAttributeBuilder(classCtorInfo, new object[0], propertyInfos, propertyValues.ToArray());
            typeBuilder.SetCustomAttribute(customAttributeBuilder);
        }

        private void SetPropertys(ref TypeBuilder typeBuilder)
        {
            foreach (var pinfo in _properties)
            {
                var propertyName = pinfo.PropertyName;
                var propertyType = pinfo?.PropertyType ?? typeof(object);
                //设置字段
                var field = typeBuilder.DefineField($"_{FirstCharToLower(propertyName)}", propertyType,
                    FieldAttributes.Private);
                var firstCharToUpper = FirstCharToUpper(propertyName);
                //设置属性方法
                var methodGet = typeBuilder.DefineMethod($"Get{firstCharToUpper}", MethodAttributes.Public,
                    propertyType, null);
                var methodSet = typeBuilder.DefineMethod($"Set{firstCharToUpper}", MethodAttributes.Public, null,
                    new Type[] { propertyType });

                var ilOfGet = methodGet.GetILGenerator();
                ilOfGet.Emit(OpCodes.Ldarg_0);
                ilOfGet.Emit(OpCodes.Ldfld, field);
                ilOfGet.Emit(OpCodes.Ret);

                var ilOfSet = methodSet.GetILGenerator();
                ilOfSet.Emit(OpCodes.Ldarg_0);
                ilOfSet.Emit(OpCodes.Ldarg_1);
                ilOfSet.Emit(OpCodes.Stfld, field);
                ilOfSet.Emit(OpCodes.Ret);

                //设置属性
                var propertyBuilder =
                    typeBuilder.DefineProperty(propertyName, PropertyAttributes.None, propertyType, null);
                propertyBuilder.SetGetMethod(methodGet);
                propertyBuilder.SetSetMethod(methodSet);

                foreach (var pinfoAttribute in pinfo.Attributes)
                {
                    //设置特性
                    SetPropertyAttribute(ref propertyBuilder, pinfoAttribute);
                }
            }
        }

        private void SetPropertyAttribute<T>(ref PropertyBuilder propertyBuilder, T tAttribute)
        {
            if (tAttribute == null)
                return;

            var propertyValues = new ArrayList();
            foreach (var propertyInfo in tAttribute.GetType().GetProperties().Where(p => p.CanWrite == true))
            {
                propertyValues.Add(propertyInfo.GetValue(tAttribute));
            }

            var propertyInfos = tAttribute.GetType().GetProperties().Where(p => p.CanWrite == true).ToArray();
            var constructor = tAttribute.GetType().GetConstructor(new Type[] { });
            var customAttributeBuilder =
                new CustomAttributeBuilder(constructor, new object[0], propertyInfos, propertyValues.ToArray());
            propertyBuilder.SetCustomAttribute(customAttributeBuilder);
        }

        /// <summary>
        /// Emit动态创建出Class - Type
        /// </summary>
        /// <returns></returns>
        public Type Build()
        {
            //初始化AssemblyName的一个实例
            var assemblyName = new AssemblyName("FreeSql.DynamicCompileBuilder");
            //设置程序集的名称
            var defineDynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            //动态在程序集内创建一个模块
            var defineDynamicModule =
                defineDynamicAssembly.DefineDynamicModule("FreeSql.DynamicCompileBuilder.Dynamics");
            //动态的在模块内创建一个类
            var typeBuilder = defineDynamicModule.DefineType(_className, TypeAttributes.Public | TypeAttributes.Class);

            //设置TableAttribute
            SetTableAttribute(ref typeBuilder);

            //设置属性
            SetPropertys(ref typeBuilder);

            //创建类的Type对象
            return typeBuilder.CreateType();
        }

        //委托缓存
        private static ConcurrentDictionary<string, Delegate>
            _delegateCache = new ConcurrentDictionary<string, Delegate>();

        //设置动态对象的属性值，ColumnInfo SetValue
        public static object CreateObjectByType1(Type type, Dictionary<string, object> porpertys)
        {
            if (type == null)
                return null;
            object istance = Activator.CreateInstance(type);
            if (istance == null)
                return null;

            foreach (var keyValuePair in porpertys)
            {
                ColumnInfo columnInfo = new ColumnInfo();
                columnInfo.CsName = keyValuePair.Key;
                columnInfo.SetValue(istance, keyValuePair.Value);
            }
            return istance;
        }

        //设置动态对象的属性值，通过表达式目录树
        public static object CreateObjectByType(Type type, Dictionary<string, object> porpertys)
        {
            if (type == null)
                return null;
            object istance = Activator.CreateInstance(type);
            if (istance == null)
                return null;
            //根据字典中的key确定缓存
            var cacheKeyStr = string.Join("-", porpertys.Keys.OrderBy(s => s));
            var cacheKey = Md5Encryption(cacheKeyStr);

            var dynamicDelegate = _delegateCache.GetOrAdd(cacheKey, key =>
            {
                //表达式目录树构建委托
                var typeParam = Expression.Parameter(type);
                var dicParamType = typeof(Dictionary<string, object>);
                var dicParam = Expression.Parameter(dicParamType);
                var exps = new List<Expression>();
                var tempRef = Expression.Variable(typeof(object));
                foreach (var pinfo in porpertys)
                {
                    var propertyInfo = type.GetProperty(pinfo.Key);
                    if (propertyInfo == null)
                        continue;
                    var propertyName = Expression.Constant(pinfo.Key, typeof(string));
                    exps.Add(Expression.Call(dicParam, dicParamType.GetMethod("TryGetValue"), propertyName, tempRef));
                    exps.Add(Expression.Assign(Expression.MakeMemberAccess(typeParam, propertyInfo),
                        Expression.Convert(tempRef, propertyInfo.PropertyType)));
                    exps.Add(Expression.Assign(tempRef, Expression.Default(typeof(object))));
                }

                var returnTarget = Expression.Label(type);
                exps.Add(Expression.Return(returnTarget, typeParam));
                exps.Add(Expression.Label(returnTarget, Expression.Default(type)));
                var block = Expression.Block(new[] { tempRef }, exps);
                var @delegate = Expression.Lambda(block, typeParam, dicParam).Compile();
                return @delegate;
            });
            var dynamicInvoke = dynamicDelegate.DynamicInvoke(istance, porpertys);
            return dynamicInvoke;
        }
        /// <summary>
        /// 首字母小写
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private string FirstCharToLower(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            string str = input.First().ToString().ToLower() + input.Substring(1);
            return str;
        }

        /// <summary>
        /// 首字母大写
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private string FirstCharToUpper(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            string str = input.First().ToString().ToUpper() + input.Substring(1);
            return str;
        }

        private static string Md5Encryption(string inputStr)
        {
            var result = string.Empty;
            //32位大写
            using (var md5 = MD5.Create())
            {
                var resultBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(inputStr));
                result = BitConverter.ToString(resultBytes);
            }
            return result;
        }
    }

    internal class DynamicPropertyInfo
    {
        public string PropertyName { get; set; } = string.Empty;
        public Type PropertyType { get; set; }
        public Attribute[] Attributes { get; set; }
    }
}