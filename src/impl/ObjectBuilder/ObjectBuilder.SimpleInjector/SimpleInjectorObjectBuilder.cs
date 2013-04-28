using System;
using System.Collections.Generic;

namespace NServiceBus.ObjectBuilder.SimpleInjector
{
	using System.Linq;
	using System.Linq.Expressions;
	using Common;
	using global::SimpleInjector;
	using global::SimpleInjector.Extensions;
	using global::SimpleInjector.Extensions.LifetimeScoping;

	/// <summary>
	/// ObjectBuilder implementation for the SimpleInjector IoC-Container
	/// </summary>
	public class SimpleInjectorObjectBuilder : IContainer
	{
		readonly object locker = new object();
		bool locked;

		readonly Container container;
		LifetimeScope scope;
		bool disposed;

		///<summary>
		/// Instantites the class with an empty SimpleInjector container.
		///</summary>
		public SimpleInjectorObjectBuilder() : this(null, null)
		{
		}

		///<summary>
		/// Instantiates the class utilizing the given LifetimeScope.
		///</summary>
		///<param name="scope"></param>
		public SimpleInjectorObjectBuilder(LifetimeScope scope) : this(null, scope)
		{
		}

		///<summary>
		/// Instantiates the class utilizing the given container.
		///</summary>
		///<param name="container"></param>
		///<param name="scope"></param>
		private SimpleInjectorObjectBuilder(Container container, LifetimeScope scope)
		{
			this.container = container ?? new Container();
			this.container.Options.PropertySelectionBehavior = new PropertyInjector(this.container); 
			//this.container.Options.AllowOverridingRegistrations = true; 
			this.container.EnableLifetimeScoping();
			
			this.scope = scope;// ?? this.container.BeginLifetimeScope();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Disposes the container and all resources instantiated by the container.
		/// </summary>
		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
			{
				return;
			}

			if (disposing && scope != null)
			{
				scope.Dispose();
			}

			disposed = true;
		}

		~SimpleInjectorObjectBuilder()
		{
			Dispose(false);
		}

		///<summary>
		/// Build an instance of a given type.
		///</summary>
		///<param name="typeToBuild"></param>
		///<returns></returns>
		public object Build(Type typeToBuild)
		{
			BeginLifetimeScope();
			return container.GetInstance(typeToBuild);
		}

		public IContainer BuildChildContainer()
		{
			BeginLifetimeScope();
			return new SimpleInjectorObjectBuilder();
		}

		///<summary>
		/// Build all instances of a given type.
		///</summary>
		///<param name="typeToBuild"></param>
		///<returns></returns>
		public IEnumerable<object> BuildAll(Type typeToBuild)
		{
			BeginLifetimeScope();
			var b = HasComponent(typeToBuild);
			var reg = container.GetCurrentRegistrations().Where(x => x.ServiceType == typeToBuild).ToList();
			var inst = container.GetInstance(typeToBuild);
			yield return inst;

			//TODO: This is hideousely broken: https://simpleinjector.codeplex.com/discussions/441869
//			var result = container.GetAllInstances(typeToBuild).ToList();
//			return result;
		}

		public void Configure(Type component, DependencyLifecycle dependencyLifecycle)
		{
			var types = GetAllServices(component); //.Where(type => !HasComponent(type));
			foreach (var type in types)
			{
				container.Register(type, component, Translate(dependencyLifecycle));
			}
		}

		public void Configure<T>(Func<T> component, DependencyLifecycle dependencyLifecycle)
		{
			if (HasComponent(typeof (T)))
				return;

			//https://simpleinjector.codeplex.com/discussions/441823
			var registration =
				Translate(dependencyLifecycle)
					.CreateRegistration(typeof (T), () => component(), container);

			container.AddRegistration(typeof (T), registration);
		}

		static IEnumerable<Type> GetAllServices(Type type)
		{
			yield return type;

			foreach (var @interface in type.GetInterfaces())
			{
				foreach (var service in GetAllServices(@interface))
				{
					yield return service;
				}
			}
		}

		Lifestyle Translate(DependencyLifecycle dependencyLifecycle)
		{
			switch (dependencyLifecycle)
			{
				case DependencyLifecycle.InstancePerCall:
					return Lifestyle.Transient;
				case DependencyLifecycle.SingleInstance:
					return Lifestyle.Singleton;
				case DependencyLifecycle.InstancePerUnitOfWork:
					return new LifetimeScopeLifestyle(disposeInstanceWhenLifetimeScopeEnds: true);
				default:
					throw new ArgumentException("Unhandled lifecycle - " + dependencyLifecycle);
			}
		}

		public void ConfigureProperty(Type component, string property, object value)
		{
			if (value == null)
			{
				return;
			}

			ConfigureProperty(container, component, property, value);
		}

		private static void ConfigureProperty(Container container,
		                                     Type component, string property, object value)
		{
			var prop = component.GetProperty(property);

			if (value == null) throw new ArgumentNullException("value");
			if (prop == null) throw new ArgumentException("property '" + property + "' not found");
			if (!prop.PropertyType.IsInstanceOfType(value))
				throw new ArgumentException("value type is incorrect", "value");

			var actionType = typeof (Action<>).MakeGenericType(component);

			var parameter = Expression.Parameter(component);

			var action = Expression.Lambda(actionType,
			                               Expression.Assign(
				                               Expression.Property(parameter, property),
				                               Expression.Constant(value)),
			                               parameter)
			                       .Compile();

			var initializer = typeof (Container).GetMethod("RegisterInitializer")
			                                    .MakeGenericMethod(component);

			initializer.Invoke(container, new[] {action});
		}

		///<summary>
		/// Register a singleton instance of a dependency.
		///</summary>
		///<param name="lookupType"></param>
		///<param name="instance"></param>
		public void RegisterSingleton(Type lookupType, object instance)
		{
			container.RegisterSingle(lookupType, instance);
		}

		public bool HasComponent(Type componentType)
		{
			return container.GetCurrentRegistrations().Any(x => x.ServiceType == componentType);
		}

		/// <summary>Prevents any new registrations to be made to the container.</summary>
		internal void BeginLifetimeScope()
		{
			if (!locked)
			{
				// By using a lock, we have the certainty that all threads will see the new value for 'locked'
				// immediately, since ThrowWhenContainerIsLocked also locks on 'locker'.
				lock (locker)
				{
					if (!locked)
					{
						locked = true;
						container.BeginLifetimeScope(); 
					}
				}
			}
		}
	}
}
