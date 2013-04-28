namespace NServiceBus.ObjectBuilder.SimpleInjector
{
	using System;
	using System.Reflection;
	using global::SimpleInjector;
	using global::SimpleInjector.Advanced;

	public class PropertyInjector : IPropertySelectionBehavior
	{
		readonly Container container;

		public PropertyInjector(Container container)
		{
			this.container = container;
		}

		public bool SelectProperty(Type serviceType, PropertyInfo propertyInfo)
		{
			return this.container.GetRegistration(propertyInfo.PropertyType, false) != null;
		}
	}
}