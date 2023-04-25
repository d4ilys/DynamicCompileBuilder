using FreeSql.DataAnnotations;

namespace DynamicBuilder
{
    [Table(Name = "xxx")]
    public class  Tests
    {
        [Column(IsPrimary = true)]
        public int id
        {
            get; set;
        }

        [Column(StringLength = 200)]
        public string name
        {
            get; set;
        }
    }
}
