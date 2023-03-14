using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lamar.Testing.Bugs
{
    public class Bug_tbd_singleton_scope_incorrect_multiple_nested_containers
    {
        public class SingletonType
        {
            public Guid Id;
            public SingletonType() { Id = Guid.NewGuid(); }
        }

        public class ScopedA
        {
            public SingletonType SingletonType;
            public ScopedA(SingletonType singletonType) { SingletonType = singletonType; }
        }

        public class ScopedB
        {
            public ScopedA A;
            public ScopedB(ScopedA scopedA) { A = scopedA; }
        }

        [Fact]
        public void SingletonScopeIsIncorrect_WhenNestedContainersAre3LevelsDeep()
        {
            var container = new Container(c =>
            {
                c.AddSingleton<SingletonType>();

                c.AddScoped<ScopedA>(); // Depends on SingletonType
                c.AddScoped<ScopedB>(); // Depends on ScopedA
            });

            var singleton = container.GetInstance<SingletonType>();

            using var nested1 = container.GetNestedContainer();
            using var nested2 = nested1.GetInstance<IContainer>().GetNestedContainer();
            using var nested3 = nested2.GetInstance<IContainer>().GetNestedContainer();

            var scopedB = nested3.GetInstance<ScopedB>();
            singleton.ShouldBeTheSameAs(scopedB.A.SingletonType);
        }

    }
}
