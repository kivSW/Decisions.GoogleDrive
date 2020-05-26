﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Decisions.GoogleDrive;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;
using File = Google.Apis.Drive.v3.Data.File;

namespace Decisions.GoogleDrive
{
    public static partial class Drive
    {
        public static bool DoesFileExist(Connection connection, DriveFile file)
        {
            if (!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");
            var request = connection.Service.Files.Get(file.Id);

            try
            {
                _ = request.Execute();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static DriveFile[] GetFiles(Connection connection, DriveFolder folder = null)
        {
            string folderId = folder == null ? "root" : folder.Id;

            if (!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");

            FilesResource.ListRequest listRequest = connection.Service.Files.List();
            listRequest.Q = $"mimeType != 'application/vnd.google-apps.folder' and '{folderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name, mimeType, description, webViewLink)";
            var fileList = listRequest.Execute();
            List<File> files = fileList.Files.ToList();
            while (fileList.NextPageToken != null)
            {
                listRequest.PageToken = fileList.NextPageToken;
                fileList = listRequest.Execute();
                files.AddRange(fileList.Files);
            }
            DriveFile[] tmp = files.Select(t => new DriveFile(t.Id, t.Name, t.Description, t.WebViewLink)).ToArray();
            return tmp;
        }

        public static void DeleteFile(Connection connection, DriveFile file)
        {
            if (!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");

            var request = connection.Service.Files.Delete(file.Id);
            var response = request.Execute();
            var _ = response.Length;//FIXME

        }

        public static DrivePermission[] GetFilePermissions(Connection connection, DriveFile file)
        {
            if (!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");
            var req = connection.Service.Files.Get(file.Id);
            req.Fields = "permissions";
            var response = req.Execute();

            if(response.Permissions == null)
                return new DrivePermission[] {};

            return response.Permissions.Select(x => new DrivePermission(x.Id, x.EmailAddress, TranslatePermType(x.Type), TranslateRole(x.Role))).ToArray();
        }

        public static DrivePermission SetFilePermissions(Connection connection, DriveFile file, DrivePermission permission /*, DrivePermType type,
            DriveRole role, string email = null*/)
        {
            if (!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");
            if (permission.Type != DrivePermType.anyone && permission.Email == null)
                throw new Exception("This permission type requires an email.");
            if (permission.Type == DrivePermType.unknown || permission.Role == DriveRole.unknown)
                throw new Exception("Invalid arguments passed.");
            if (!DoesFileExist(connection, file))
                throw new Exception("File wasn't found.");

            var request = connection.Service.Permissions.Create(new Permission()
            {
                EmailAddress = permission.Email,
                Type = permission.Type.ToString(),
                Role = permission.Role.ToString()
            }, file.Id);

            var resp = request.Execute();
            return new DrivePermission(resp.Id, resp.EmailAddress, permission.Type, permission.Role);
        }

        public static bool DownloadFile(Connection connection, DriveFile file, Stream output, Action<IDownloadProgress> progressTracker = null)
        {
            if(!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");
            if(!DoesFileExist(connection, file))
                throw new Exception("File does not exist.");

            var request = connection.Service.Files.Get(file.Id);
            if(progressTracker != null)
                request.MediaDownloader.ProgressChanged += progressTracker;
            var resp = request.DownloadWithStatus(output);
            if (resp.Status == DownloadStatus.Completed)
                return true;
            return false;
        }

        public static DriveFile UploadFile(Connection connection, Stream stream, string name, DriveFolder parent = null, Action<IUploadProgress> progessUpdate = null)
        {
            if (!connection.IsConnected() || connection.Service == null)
                throw new Exception("Invalid connection object.");
            if(string.IsNullOrEmpty(name))
                throw new Exception("Name cannot be null or empty.");

            var fileMetadata = new File()
            {
                Name = name,
                MimeType = MimeMapping.GetMimeMapping(name),
                Parents = new List<string> {parent != null ? parent.Id : "root"}
            };
            var request = connection.Service.Files.Create(fileMetadata, stream, MimeMapping.GetMimeMapping(name));
            request.Fields = "id, name, mimeType, description, webViewLink";
            if (progessUpdate != null)
                request.ProgressChanged += progessUpdate;
            var result = request.Upload();
            if (result.Status == UploadStatus.Completed)
            {
                var file = request.ResponseBody;
                if (file != null)
                {
                    return new DriveFile(file.Id, file.Name, file.Description, file.WebViewLink);
                }
            }

            return null;
        }
    }
}