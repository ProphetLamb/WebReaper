﻿using StackExchange.Redis;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker;

public class RedisCrawledLinkTracker : ICrawledLinkTracker
{
    private static ConnectionMultiplexer? redis;

    public RedisCrawledLinkTracker(string connectionString)
    {
        redis = ConnectionMultiplexer.Connect(connectionString);
    }

    public async Task AddVisitedLinkAsync(string siteUrl, string visitedLink)
    {
        IDatabase db = redis.GetDatabase();
        await db.SetAddAsync(siteUrl, visitedLink);
    }

    public async Task<IEnumerable<string>> GetVisitedLinksAsync(string siteUrl)
    {
        IDatabase db = redis.GetDatabase();
        var result = await db.SetMembersAsync(siteUrl);

        return result.Select(x => x.ToString());
    }
}