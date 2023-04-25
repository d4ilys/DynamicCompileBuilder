# DynamicCompileBuilder
通过Emit和ExpressionTree动态构建类、属性赋值

可以用于ORM动态构建Table

~~~c#

//通过Emit动态构建Type
var type = fsql.CodeFirst
    .DynamicEntity("TestClass", new TableAttribute { Name = "TB_Test" })
    .Property("Id", typeof(int), new ColumnAttribute { IsIdentity = true, IsPrimary = true, Position = 1 }, new NavigateAttribute(""))
    .Property("Name", typeof(string), new ColumnAttribute() { Name = "CustomName", StringLength = 20, Position = 2 })
    .Property("Age", typeof(int), new ColumnAttribute() { Position = 3 })
    .Build();
//根据Type生成表
fsql.CodeFirst.SyncStructure(type);
//动态赋值
var instance = type.CreateDynamicEntityInstance(new Dictionary<string, object>()
{
     { "Age", 21 },
     { "Name", "张三" }
});
fsql.Insert<object>().AsType(type).AppendData(instance).ExecuteAffrows();
Console.ReadLine();

~~~

