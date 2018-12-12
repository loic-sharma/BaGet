namespace BaGet.Core.Entities
{
    public abstract class EntityBase : IEntity
    {
        public virtual int Id { get; set; }
        public virtual byte[] RowVersion { get; set; }
    }
}
