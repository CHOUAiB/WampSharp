using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WampSharp.Core.Serialization;
using WampSharp.Core.Utilities;
using WampSharp.V2.Core.Contracts;

namespace WampSharp.V2.PubSub
{
    internal abstract class MatchTopicContainer
    {
        #region Fields

        private readonly ConcurrentDictionary<string, WampTopic> mTopicUriToSubject;
        private readonly object mLock = new object();

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new instance of <see cref="WampTopicContainer"/>.
        /// </summary>
        public MatchTopicContainer()
        {
            mTopicUriToSubject =
                new ConcurrentDictionary<string, WampTopic>();
        }

        #endregion

        #region Public Methods

        public IEnumerable<string> TopicUris
        {
            get { return mTopicUriToSubject.Keys; }
        }

        public IEnumerable<IWampTopic> Topics
        {
            get { return mTopicUriToSubject.Values; }
        }

        public IDisposable Subscribe(IWampRawTopicRouterSubscriber subscriber, string topicUri, SubscribeOptions options)
        {
            lock (mLock)
            {
                IWampTopic topic = GetOrCreateTopicByUri(topicUri);

                return topic.Subscribe(subscriber);
            }
        }

        public bool Publish<TMessage>(IWampFormatter<TMessage> formatter,
                                      long publicationId,
                                      PublishOptions options,
                                      string topicUri)
        {
            return PublishSafe(topicUri,
                               topic =>
                                   topic.Publish(formatter, publicationId, options));
        }

        public bool Publish<TMessage>(IWampFormatter<TMessage> formatter,
                                      long publicationId,
                                      PublishOptions options,
                                      string topicUri,
                                      TMessage[] arguments)
        {
            return PublishSafe(topicUri,
                               topic =>
                                   topic.Publish(formatter, publicationId, options, arguments));
        }

        public bool Publish<TMessage>(IWampFormatter<TMessage> formatter,
                                      long publicationId,
                                      PublishOptions options,
                                      string topicUri,
                                      TMessage[] arguments,
                                      IDictionary<string, TMessage> argumentKeywords)
        {
            return PublishSafe(topicUri,
                               topic =>
                                   topic.Publish(formatter, publicationId, options, arguments, argumentKeywords));
        }

        private bool PublishSafe
            (string topicUri, Action<IWampTopic> invoker)
        {
            lock (mLock)
            {
                bool anyTopics = false;

                IEnumerable<IWampTopic> topics = GetMatchingTopics(topicUri);

                foreach (IWampTopic topic in topics)
                {
                    anyTopics = true;
                    invoker(topic);
                }

                return anyTopics;
            }
        }

        public IWampTopic CreateTopicByUri(string topicUri, bool persistent)
        {
            WampTopic wampTopic = CreateWampTopic(topicUri, persistent);

            IDictionary<string, WampTopic> casted = mTopicUriToSubject;

            casted.Add(topicUri, wampTopic);

            RaiseTopicCreated(wampTopic);

            return wampTopic;
        }

        public IWampTopic GetOrCreateTopicByUri(string topicUri)
        {
            // Pretty ugly.
            bool created = false;

            WampTopic result =
                mTopicUriToSubject.GetOrAdd(topicUri,
                    key =>
                    {
                        WampTopic topic = CreateWampTopic(topicUri, false);
                        created = true;
                        return topic;
                    });

            if (created)
            {
                RaiseTopicCreated(result);
            }

            return result;
        }

        public IWampTopic GetTopicByUri(string topicUri)
        {
            WampTopic result;

            if (mTopicUriToSubject.TryGetValue(topicUri, out result))
            {
                return result;
            }

            return null;
        }

        public bool TryRemoveTopicByUri(string topicUri, out IWampTopic topic)
        {
            WampTopic value;
            bool result = mTopicUriToSubject.TryRemove(topicUri, out value);
            topic = value;

            if (result)
            {
                RaiseTopicRemoved(topic);
            }

            return result;
        }

        #endregion

        #region Private Methods

        private WampTopic CreateWampTopic(string topicUri, bool persistent)
        {
            WampTopic topic = new WampTopic(topicUri, persistent);

            // Non persistent topics die when they are empty :)
            if (!persistent)
            {
                topic.TopicEmpty += OnTopicEmpty;
            }

            return topic;
        }

        private void OnTopicEmpty(object sender, EventArgs e)
        {
            lock (mLock)
            {
                WampTopic topic = sender as WampTopic;

                if (!topic.HasSubscribers)
                {
                    topic.TopicEmpty -= OnTopicEmpty;
                    topic.Dispose();

                    if (mTopicUriToSubject.TryRemoveExact(topic.TopicUri, topic))
                    {
                        RaiseTopicRemoved(topic);
                    }
                }
            }
        }

        #endregion

        #region Events

        public event EventHandler<WampTopicCreatedEventArgs> TopicCreated;

        public event EventHandler<WampTopicRemovedEventArgs> TopicRemoved;

        private void RaiseTopicCreated(IWampTopic wampTopic)
        {
            EventHandler<WampTopicCreatedEventArgs> topicCreated = TopicCreated;

            if (topicCreated != null)
            {
                topicCreated(this, new WampTopicCreatedEventArgs(wampTopic));
            }
        }

        private void RaiseTopicRemoved(IWampTopic topic)
        {
            EventHandler<WampTopicRemovedEventArgs> topicRemoved = TopicRemoved;

            if (topicRemoved != null)
            {
                topicRemoved(this, new WampTopicRemovedEventArgs(topic));
            }
        }

        #endregion

        #region Abstract Methods

        public abstract IWampCustomizedSubscriptionId GetSubscriptionId(string topicUri, SubscribeOptions options);

        protected abstract IEnumerable<IWampTopic> GetMatchingTopics(string criteria);

        public abstract bool Handles(SubscribeOptions options);

        #endregion
    }
}