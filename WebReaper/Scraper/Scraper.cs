﻿using System.Net;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Security;
using WebReaper.Domain.Selectors;
using WebReaper.Absctracts.Sinks;
using WebReaper.Abastracts.Spider;
using WebReaper.Abstractions.Parsers;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Parser;
using WebReaper.Queue;
using WebReaper.Sinks;
using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker.Concrete;
using WebReaper.Loaders;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebReaper.Scraper;

public class Scraper
{
    public List<IScraperSink> Sinks { get; protected set; } = new(); 

    protected List<LinkPathSelector> linkPathSelectors = new();
    
    protected int limit = int.MaxValue;

    protected BlockingCollection<Job> jobs = new(new ProducerConsumerPriorityQueue());

    private string? startUrl;
    
    private ISpider spider;

    private Schema? schema;

    private WebProxy? proxy;

    private WebProxy[] proxies = Array.Empty<WebProxy>();

    protected string baseUrl = "";

    protected readonly IJobQueueReader JobQueueReader;

    protected readonly IJobQueueWriter JobQueueWriter;

    protected ILogger Logger = NullLogger.Instance;

    protected ILinkParser LinkParser = new LinkParserByCssSelector();

    protected ILinkTracker SiteLinkTracker = new InMemoryLinkTracker();

    protected IContentParser ContentParser;

    protected static SocketsHttpHandler httpHandler = new()
    {
        MaxConnectionsPerServer = 100,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Leave certs unvalidated for debugging
            RemoteCertificateValidationCallback = delegate { return true; },
        },
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan
    };

    protected Lazy<HttpClient> httpClient = new(() => new(httpHandler));

    protected List<string> urlBlackList = new();

    protected int ParallelismDegree { get; private set; } = 1;

    public Scraper()
    {
        JobQueueReader = new JobQueueReader(jobs);
        JobQueueWriter = new JobQueueWriter(jobs);
    }

    public Scraper WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }
    public Scraper WithStartUrl(string startUrl)
    {
        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        var baseUrl = startUri.GetLeftPart(UriPartial.Authority);
        var segments = startUri.Segments;

        this.baseUrl = baseUrl + string.Join(string.Empty, segments.SkipLast(1));

        return this;
    }

    public Scraper Authorize(Func<CookieContainer> authorize)
    {
        var CookieContainer = authorize();

        httpHandler.CookieContainer = CookieContainer;

        return this;
    }

    public Scraper FollowLinks(
        string linkSelector,
        SelectorType selectorType = SelectorType.Css,
        PageType pageType = PageType.Static)
    {
        linkPathSelectors.Add(new(linkSelector, SelectorType: selectorType, PageType: pageType));
        return this;
    }

    public Scraper FollowLinks(string linkSelector, string paginationSelector, SelectorType selectorType = SelectorType.Css, PageType pageType = PageType.Static)
    {
        linkPathSelectors.Add(new(linkSelector, SelectorType: selectorType, PageType: pageType));
        return this;
    }

    public Scraper AddSink(IScraperSink sink)
    {
        this.Sinks.Add(sink);

        return this;
    }

    public Scraper IgnoreUrls(params string[] urls)
    {
        this.urlBlackList.AddRange(urls);
        return this;
    }

    public Scraper WithScheme(Schema schema)
    {
        this.schema = schema;
        return this;
    }

    public Scraper WithParallelismDegree(int parallelismDegree)
    {
        this.ParallelismDegree = parallelismDegree;
        return this;
    }

    public Scraper Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public Scraper WithProxy(WebProxy proxy)
    {
        this.proxy = proxy;
        return this;
    }

    public Scraper WithProxy(WebProxy[] proxies)
    {
        this.proxies = proxies;
        return this;
    }

    public async Task Run()
    {
        JobQueueWriter.Write(new Job(
            schema!,
            baseUrl,
            startUrl!,
            ImmutableQueue.Create<LinkPathSelector>(linkPathSelectors.ToArray()),
            DepthLevel: 0));

        var spiderTasks = Enumerable
            .Range(0, ParallelismDegree)
            .Select(_ => spider.CrawlAsync());

        await Task.WhenAll(spiderTasks);
    }

    public Scraper WriteToConsole() => this.AddSink(new ConsoleSink());

    public Scraper WriteToJsonFile(string filePath) => this.AddSink(new JsonFileSink(filePath));

    public Scraper WriteToCsvFile(string filePath) => this.AddSink(new CsvFileSink(filePath));

    public Scraper Build()
    {
        ArgumentNullException.ThrowIfNull(startUrl);
        ArgumentNullException.ThrowIfNull(schema);

        ContentParser = new ContentParser(Logger);

        spider = new WebReaper.Spider.Spider(
            Sinks,
            LinkParser,
            ContentParser,
            SiteLinkTracker,
            new HttpPageLoader(httpClient.Value, Logger),
            new PuppeteerPageLoader(Logger),
            JobQueueReader,
            JobQueueWriter,
            Logger);
        
        spider.UrlBlackList = urlBlackList.ToList();

        spider.PageCrawlLimit = limit;

        return this;
    }
}
