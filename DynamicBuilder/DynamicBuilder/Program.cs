using FreeSql;
using FreeSql.DataAnnotations;
using FreeSql.Extensions;
using FreeSql.Internal;
using FreeSql.Internal.Model;
using Newtonsoft.Json;
using ZSpitz.Util;

namespace DynamicBuilder;

public class Program
{
    private static IFreeSql fsql = new FreeSqlBuilder().UseConnectionString(DataType.PostgreSQL,
            "Host=192.168.0.36;Port=5432;Username=postgres;Password=123; Database=tsp_log_202304;ArrayNullabilityMode=Always;Pooling=true;Minimum Pool Size=1")
        .UseMonitorCommand(d => Console.WriteLine(d.CommandText)).Build();

    public static void Main(string[] args)
    {
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
    }
}