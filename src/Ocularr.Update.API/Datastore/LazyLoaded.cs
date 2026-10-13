using System;
using System.Threading.Tasks;

namespace Ocularr.Update.API.Datastore
{
    public interface ILazyLoaded : ICloneable
    {
        bool IsLoaded { get; }
        void Prepare(IDatabase database, object parent);
        Task LoadAsync();
    }

    public class LazyLoaded<TChild> : ILazyLoaded
    {
        protected TChild _value;

        public LazyLoaded()
        {
        }

        public LazyLoaded(TChild val)
        {
            _value = val;
            IsLoaded = true;
        }

        public TChild Value => _value;

        public bool IsLoaded { get; protected set; }

        public static implicit operator LazyLoaded<TChild>(TChild val) => new(val);

        public static implicit operator TChild(LazyLoaded<TChild> lazy) => lazy.Value;

        public virtual void Prepare(IDatabase database, object parent)
        {
        }

        public virtual Task LoadAsync() => Task.CompletedTask;

        public object Clone() => MemberwiseClone();

        public bool ShouldSerializeValue() => IsLoaded;
    }

    internal class LazyLoaded<TParent, TChild> : LazyLoaded<TChild>
    {
        private readonly Func<IDatabase, TParent, Task<TChild>> _query;
        private readonly Func<TParent, bool> _condition;
        private IDatabase _database;
        private TParent _parent;

        public LazyLoaded(TChild val)
            : base(val)
        {
        }

        internal LazyLoaded(Func<IDatabase, TParent, Task<TChild>> query, Func<TParent, bool> condition = null)
        {
            _query = query;
            _condition = condition;
        }

        public static implicit operator LazyLoaded<TParent, TChild>(TChild val) => new(val);

        public static implicit operator TChild(LazyLoaded<TParent, TChild> lazy) => lazy.Value;

        public override void Prepare(IDatabase database, object parent)
        {
            _database = database;
            _parent = (TParent)parent;
        }

        public override async Task LoadAsync()
        {
            if (IsLoaded)
            {
                return;
            }

            _value = _condition != null && _condition(_parent)
                ? await _query(_database, _parent).ConfigureAwait(false)
                : default;

            IsLoaded = true;
        }
    }

    public static class LazyLoadedExtensions
    {
        public static async Task<TChild> LoadAsync<TChild>(this LazyLoaded<TChild> lazyLoaded)
        {
            await lazyLoaded.LoadAsync().ConfigureAwait(false);
            return lazyLoaded.Value;
        }
    }
}
