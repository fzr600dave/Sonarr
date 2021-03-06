﻿using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using NLog;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Security
{
    public static class X509CertificateValidationPolicy
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger();

        public static void Register()
        {
            ServicePointManager.ServerCertificateValidationCallback = ShouldByPassValidationError;
        }

        private static bool ShouldByPassValidationError(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            var request = sender as HttpWebRequest;

            if (request == null)
            {
                return true;
            }

            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            Logger.Debug("Certificate validation for {0} failed. {1}", request.Address, sslPolicyErrors);

            return true;
        }
    }
}