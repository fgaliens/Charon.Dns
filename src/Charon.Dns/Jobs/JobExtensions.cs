using Microsoft.Extensions.DependencyInjection;

namespace Charon.Dns.Jobs;

public static class JobExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddJobs(Action<JobConfigurer> configureJobs)
        {
            services.AddSingleton<IJobRunner, JobRunner>();
            var jobConfigurer = new JobConfigurer(services);
            configureJobs(jobConfigurer);
            return services;
        }
    }
}

public readonly record struct JobConfigurer(IServiceCollection Services)
{
    public JobConfigurer AddJob<T>() where T : class, IJob
    {
        Services.AddTransient<IJob, T>();
        return this;
    }
}
