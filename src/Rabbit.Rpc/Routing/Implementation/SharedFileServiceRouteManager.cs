﻿using Microsoft.Extensions.Logging;
using Rabbit.Rpc.Address;
using Rabbit.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rabbit.Rpc.Routing.Implementation
{
    /// <summary>
    /// 基于共享文件的服务路由管理者。
    /// </summary>
    //todo:netcore还不支持文件变更即时更新
    public class SharedFileServiceRouteManager : IServiceRouteManager, IDisposable
    {
        #region Field

        private readonly string _filePath;
        private readonly ISerializer<string> _serializer;
        private readonly ILogger<SharedFileServiceRouteManager> _logger;
        private IEnumerable<ServiceRoute> _routes;
#if NET45 || NET451
        private readonly FileSystemWatcher _fileSystemWatcher;
#endif

        #endregion Field

        #region Constructor

        public SharedFileServiceRouteManager(string filePath, ISerializer<string> serializer, ILogger<SharedFileServiceRouteManager> logger)
        {
            _filePath = filePath;
            _serializer = serializer;
            _logger = logger;

#if NET45 || NET451
            var directoryName = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryName))
                _fileSystemWatcher = new FileSystemWatcher(directoryName, "*" + Path.GetExtension(filePath));

            _fileSystemWatcher.Changed += _fileSystemWatcher_Changed;
            _fileSystemWatcher.Created += _fileSystemWatcher_Changed;
            _fileSystemWatcher.Deleted += _fileSystemWatcher_Changed;
            _fileSystemWatcher.Renamed += _fileSystemWatcher_Changed;
            _fileSystemWatcher.IncludeSubdirectories = false;
            _fileSystemWatcher.EnableRaisingEvents = true;
#endif
        }

        #endregion Constructor

        #region Implementation of IServiceRouteManager

        /// <summary>
        /// 获取所有可用的服务路由信息。
        /// </summary>
        /// <returns>服务路由集合。</returns>
        public Task<IEnumerable<ServiceRoute>> GetRoutesAsync()
        {
            if (_routes == null)
                EntryRoutes(_filePath);
            return Task.FromResult(_routes);
        }

        /// <summary>
        /// 添加服务路由。
        /// </summary>
        /// <param name="routes">服务路由集合。</param>
        /// <returns>一个任务。</returns>
        public async Task AddRoutesAsync(IEnumerable<ServiceRoute> routes)
        {
            await Task.Run(() =>
            {
                lock (this)
                {
                    File.WriteAllText(_filePath, _serializer.Serialize(routes), Encoding.UTF8);
                }
            });
        }

        /// <summary>
        /// 清空所有的服务路由。
        /// </summary>
        /// <returns>一个任务。</returns>
        public Task ClearAsync()
        {
            return Task.Run(() =>
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            });
        }

        #endregion Implementation of IServiceRouteManager

        #region Private Method

        private void EntryRoutes(string file)
        {
            lock (this)
            {
                if (File.Exists(file))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug($"准备从文件：{file}中获取服务路由。");
                    var content = File.ReadAllText(file);
                    try
                    {
                        _routes = _serializer.Deserialize<string, IpAddressDescriptor[]>(content).Select(i => new ServiceRoute
                        {
                            Address = i.Address,
                            ServiceDescriptor = i.ServiceDescriptor
                        }).ToArray();
                        if (_logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation($"成功获取到以下路由信息：{string.Join(",", _routes.Select(i => i.ServiceDescriptor.Id))}。");
                    }
                    catch (Exception exception)
                    {
                        if (_logger.IsEnabled(LogLevel.Error))
                            _logger.LogError("获取路由信息时发生了错误。", exception);
                        _routes = Enumerable.Empty<ServiceRoute>();
                    }
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                        _logger.LogWarning($"无法获取路由信息，因为文件：{file}不存在。");
                    _routes = Enumerable.Empty<ServiceRoute>();
                }
            }
        }

#if NET45 || NET451

        private void _fileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation($"文件{_filePath}发生了变更，将重新获取路由信息。");
            EntryRoutes(_filePath);
        }

#endif

        #endregion Private Method

        protected class IpAddressDescriptor
        {
            public List<IpAddressModel> Address { get; set; }
            public ServiceDescriptor ServiceDescriptor { get; set; }
        }

        #region Implementation of IDisposable

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
#if NET45 || NET451
            _fileSystemWatcher?.Dispose();
#endif
        }

        #endregion Implementation of IDisposable
    }
}