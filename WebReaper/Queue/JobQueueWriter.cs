using System.Collections.Concurrent;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain;

namespace WebReaper.Queue;

public class JobQueueWriter : IJobQueueWriter
{
    private readonly BlockingCollection<Job> jobs;

    public JobQueueWriter(BlockingCollection<Job> jobs) => this.jobs = jobs;

    public void Write(Job job)
    {
        if (jobs.Any(existingJob => existingJob.Url == job.Url)) return;

        jobs.Add(job);
    }

    public int Count => jobs.Count;

    public void CompleteAdding() => jobs.CompleteAdding();
}