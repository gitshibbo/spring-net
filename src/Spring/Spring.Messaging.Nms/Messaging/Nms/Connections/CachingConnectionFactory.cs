/*
 * Copyright 2002-2010 the original author or authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Apache.NMS;
using Microsoft.Extensions.Logging;
using Spring.Messaging.Nms.Support;
using Spring.Util;

namespace Spring.Messaging.Nms.Connections;

/// <summary>
/// <see cref="SingleConnectionFactory"/> subclass that adds
/// Session, MessageProducer, and MessageConsumer caching.  This ConnectionFactory
/// also switches the ReconnectOnException property to true
/// by default, allowing for automatic recovery of the underlying
/// Connection.
/// </summary>
/// <remarks>
/// By default, only one single Session will be cached, with further requested
/// Sessions being created and disposed on demand. Consider raising the
/// SessionCacheSize property in case of a high-concurrency environment.
/// <para>
/// NOTE: This ConnectionFactory requires explicit closing of all Sessions
/// obtained from its shared Connection. This is the usual recommendation for
/// native NMS access code anyway. However, with this ConnectionFactory, its use
/// is mandatory in order to actually allow for Session reuse.
/// </para>
/// <para>
/// Note also that MessageConsumers obtained from a cached Session won't get
/// closed until the Session will eventually be removed from the pool. This may
/// lead to semantic side effects in some cases. For a durable subscriber, the
/// logical <code>Session.Close()</code> call will also close the subscription.
/// Re-registering a durable consumer for the same subscription on the same
/// Session handle is not supported; close and reobtain a cached Session first.
/// </para>
/// </remarks>
///
/// <author>Juergen Hoeller</author>
/// <author>Mark Pollack (.NET)</author>
public class CachingConnectionFactory : SingleConnectionFactory
{
    private static readonly ILogger<CachingConnectionFactory> Log = LogManager.GetLogger<CachingConnectionFactory>();

    private int sessionCacheSize = 1;

    private volatile bool active = true;

    private readonly Dictionary<AcknowledgementMode, List<ISession>> cachedSessions =
        new Dictionary<AcknowledgementMode, List<ISession>>();

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingConnectionFactory"/> class.
    /// and sets the ReconnectOnException to true
    /// </summary>
    public CachingConnectionFactory()
    {
        ReconnectOnException = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingConnectionFactory"/> class for the given
    /// IConnectionFactory
    /// </summary>
    /// <param name="targetConnectionFactory">The target connection factory.</param>
    public CachingConnectionFactory(IConnectionFactory targetConnectionFactory) : base(targetConnectionFactory)
    {
        ReconnectOnException = true;
    }

    /// <summary>
    /// Gets or sets the size of the session cache.
    /// </summary>
    /// <remarks>
    /// This cache size is the maximum limit for the number of cached Sessions
    /// per session acknowledgement type (auto, client, dups_ok, transacted).
    /// As a consequence, the actual number of cached Sessions may be up to
    /// four times as high as the specified value - in the unlikely case
    /// of mixing and matching different acknowledgement types.
    /// <para>
    /// Default is 1: caching a single Session, (re-)creating further ones on
    /// demand. Specify a number like 10 if you'd like to raise the number of cached
    /// Sessions; that said, 1 may be sufficient for low-concurrency scenarios.
    /// </para>
    /// </remarks>
    /// <value>The size of the session cache.</value>
    public int SessionCacheSize
    {
        get => sessionCacheSize;
        set
        {
            AssertUtils.IsTrue(value >= 1, "Session cache size must be 1 or higher");
            sessionCacheSize = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to cache MessageProducers per
    /// Session instance. (more specifically: one MessageProducer per Destination
    /// and Session).
    /// </summary>
    /// <remarks>
    /// <para>Default is "true". Switch this to "false" in order to always,
    /// recreate MessageProducers on demand.
    /// </para>
    /// </remarks>
    /// <value><c>true</c> if should cache message producers; otherwise, <c>false</c>.</value>
    public bool CacheProducers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether o cache JMS MessageConsumers per
    /// NMS Session instance.
    /// </summary>
    /// <remarks>
    /// Mmore specifically: one MessageConsumer per Destination, selector String
    /// and Session. Note that durable subscribers will only be cached until
    /// logical closing of the Session handle.
    /// <para>
    /// Default is "true". Switch this to "false" in order to always
    /// recreate MessageConsumers on demand.
    /// </para>
    /// </remarks>
    /// <value><c>true</c> to cache consumers per session instance; otherwise, <c>false</c>.</value>
    public bool CacheConsumers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this instance is active.
    /// </summary>
    /// <value><c>true</c> if this instance is active; otherwise, <c>false</c>.</value>
    public bool IsActive
    {
        get => active;
        set => active = value;
    }

    /// <summary>
    /// Resets the Session cache as well as resetting the connection.
    /// </summary>
    public override void ResetConnection()
    {
        this.active = false;
        lock (cachedSessions)
        {
            foreach (var pair in cachedSessions)
            {
                var sessionList = pair.Value;
                lock (sessionList)
                {
                    foreach (ISession session in sessionList)
                    {
                        try
                        {
                            session.Close();
                        }
                        catch (Exception ex)
                        {
                            Log.LogTrace(ex, "Could not close cached NMS Session");
                        }
                    }
                }
            }

            cachedSessions.Clear();
        }

        this.active = true;
        // Now proceed with actual closing of the shared Connection...
        base.ResetConnection();
    }

    /// <summary>
    /// Obtaining a cached Session.
    /// </summary>
    /// <param name="con">The connection to operate on.</param>
    /// <param name="mode">The session ack mode.</param>
    /// <returns>The Session to use
    /// </returns>
    public override ISession GetSession(IConnection con, AcknowledgementMode mode)
    {
        return GetSessionAsync(con, mode).GetAsyncResult();
    }

    public override async Task<ISession> GetSessionAsync(IConnection con, AcknowledgementMode mode)
    {
        List<ISession> sessionList;
        lock (cachedSessions)
        {
            if (!cachedSessions.TryGetValue(mode, out sessionList))
            {
                sessionList = new List<ISession>();
                cachedSessions[mode] = sessionList;
            }
        }

        ISession session = null;
        lock (sessionList)
        {
            if (sessionList.Count > 0)
            {
                session = sessionList[0];
                sessionList.RemoveAt(0);
            }
        }

        if (session != null)
        {
            if (Log.IsEnabled(LogLevel.Debug))
            {
                string message = "Found cached Session for mode " + mode + ": "
                                 + (session is IDecoratorSession decoratorSession ? decoratorSession.TargetSession : session);
                Log.LogDebug(message);
            }
        }
        else
        {
            ISession targetSession = await con.CreateSessionAsync(mode).Awaiter();
            if (Log.IsEnabled(LogLevel.Debug))
            {
                Log.LogDebug("Creating cached Session for mode " + mode + ": " + targetSession);
            }

            session = GetCachedSessionWrapper(targetSession, sessionList);
        }

        return session;
    }

    /// <summary>
    /// Wraps the given Session so that it delegates every method call to the target session but
    /// adapts close calls. This is useful for allowing application code to
    /// handle a special framework Session just like an ordinary Session.
    /// </summary>
    /// <param name="targetSession">The original Session to wrap.</param>
    /// <param name="sessionList">The List of cached Sessions that the given Session belongs to.</param>
    /// <returns>The wrapped Session</returns>
    protected virtual ISession GetCachedSessionWrapper(ISession targetSession, List<ISession> sessionList)
    {
        return new CachedSession(targetSession, sessionList, this);
    }
}
