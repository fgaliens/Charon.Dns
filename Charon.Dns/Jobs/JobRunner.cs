using Serilog;

namespace Charon.Dns.Jobs;

public class JobRunner : IJobRunner
{
    private readonly IJob[] _jobs;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ILogger _logger;

    public JobRunner(
        IEnumerable<IJob> jobs,
        ILogger logger)
    {
        _jobs = jobs.ToArray();
        _cancellationTokenSource = new CancellationTokenSource();
        _logger = logger;
    }

    public void Start()
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            throw new InvalidOperationException("Job runner has been stopped");
        }

        foreach (var job in _jobs)
        {
            Task.Run(async () =>
            {
                var localJob = job;
                var jobType = localJob.GetType();
                _logger.Debug("Job {JobName} started", jobType);
                
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        _logger.Debug("Executing job {JobName}", jobType);
                        
                        await localJob.Execute();
                        
                        _logger.Debug("Job {JobName} executed", jobType);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error in job {JobName}", jobType);
                    }
                    
                    _logger.Debug("Job {JobName} delayed for {Period}", jobType, localJob.Period);
                    await Task.Delay(localJob.Period);
                }
            }, _cancellationTokenSource.Token);
        }
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }
}