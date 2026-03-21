using Charon.Dns.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Charon.Dns.Tests.Utils.Mock;

public static class MockExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMockOf<T>(Action<Moq.Mock<T>>? configure = null) 
            where T : class
        {
            var mock = new Moq.Mock<T>();
            configure?.Invoke(mock);
            
            services.AddSingleton(mock);
            services.AddSingleton(mock.Object);
            
            return services;
        }

        public IServiceCollection AddSettings<T>(T settings) where T : class, ISettings<T>
        {
            services.AddSingleton(new SettingsStorage<T> { Settings = settings });
            services.AddTransient<T>(sp => sp.GetRequiredService<SettingsStorage<T>>().Settings!);
            return services;
        }
    }

    extension(IServiceProvider serviceProvider)
    {
        public Moq.Mock<T> GetMockOf<T>()
            where T : class
        {
            return serviceProvider.GetRequiredService<Moq.Mock<T>>();
        }
        
        public IServiceProvider SetupMockOf<T>(Action<Moq.Mock<T>> setup)
            where T : class
        {
            var mock = serviceProvider.GetMockOf<T>();
            setup(mock);
            return serviceProvider;
        }

        public IServiceProvider SetSettings<T>(T settings) where T : class, ISettings<T>
        {
            var settingsStorage = serviceProvider.GetRequiredService<SettingsStorage<T>>();
            settingsStorage.Settings = settings;
            return serviceProvider;
        }
    }

    private class SettingsStorage<T> where T : class, ISettings<T>
    {
        public T? Settings { get; set; }
    }
}
