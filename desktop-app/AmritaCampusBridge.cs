using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AmritaCampusBridge
{
    public class RequestItem
    {
        public string Id;
        public string ScholarName;
        public string Purpose;
        public int DurationMinutes;
        public string Status;
        public string SessionCode;
    }

    public class MainForm : Form
    {
        private const string DEFAULT_SERVER_URL = "https://amrita-campus-vpn.onrender.com";
        private const string AGENT_B64 = "LyoqCiAqIEFtcml0YSBDYW1wdXMgQnJpZGdlIOKAlCBIb3N0ZWxsZXIgQ2FtcHVzIFJlbGF5IEFnZW50CiAqCiAqIFJ1biB0aGlzIHNjcmlwdCBvbiBhIGxhcHRvcCBjb25uZWN0ZWQgdG8gQU1SSVRBIENPTExFR0UgV0ktRkkuCiAqIENvbm5lY3RzIG91dGJvdW5kIG92ZXIgc3RhbmRhcmQgUkZDIDY0NTUgV2ViU29ja2V0IHRvIGh0dHBzOi8vYW1yaXRhLWNhbXB1cy12cG4ub25yZW5kZXIuY29tCiAqIChvciBsb2NhbGhvc3Q6MzAwMCkgc28gaXQgd29ya3MgdGhyb3VnaCBib3RoIENvbGxlZ2UgV2ktRmkgZmlyZXdhbGxzIGFuZCBSZW5kZXIgQ2xvdWQhCiAqLwoKY29uc3QgaHR0cCA9IHJlcXVpcmUoJ2h0dHAnKTsKY29uc3QgaHR0cHMgPSByZXF1aXJlKCdodHRwcycpOwpjb25zdCBuZXQgPSByZXF1aXJlKCduZXQnKTsKY29uc3QgY3J5cHRvID0gcmVxdWlyZSgnY3J5cHRvJyk7CmNvbnN0IHsgVVJMIH0gPSByZXF1aXJlKCd1cmwnKTsKCmNvbnN0IERFRkFVTFRfUkVOREVSX1VSTCA9ICdodHRwczovL2Ftcml0YS1jYW1wdXMtdnBuLm9ucmVuZGVyLmNvbSc7CgpmdW5jdGlvbiBwYXJzZUFyZ3MoYXJndikgewogIGNvbnN0IG9wdHMgPSB7CiAgICBzZXJ2ZXI6IHByb2Nlc3MuZW52LkFNUklUQV9CUk9LRVJfVVJMIHx8IERFRkFVTFRfUkVOREVSX1VSTCwKICAgIG5hbWU6IHByb2Nlc3MuZW52LkhPU1RFTExFUl9OQU1FIHx8ICdDYW1wdXMtSG9zdGVsbGVyJywKICAgIGR1cmF0aW9uOiBOdW1iZXIocHJvY2Vzcy5lbnYuRFVSQVRJT05fTUlOUyB8fCAzMCksCiAgICBzZXNzaW9uOiAnJywKICAgIHJlcXVlc3Q6ICcnCiAgfTsKICBmb3IgKGxldCBpID0gMjsgaSA8IGFyZ3YubGVuZ3RoOyBpKyspIHsKICAgIGNvbnN0IGFyZyA9IGFyZ3ZbaV07CiAgICBpZiAoYXJnID09PSAnLS1zZXJ2ZXInICYmIGFyZ3ZbaSArIDFdKSBvcHRzLnNlcnZlciA9IGFyZ3ZbKytpXTsKICAgIGVsc2UgaWYgKGFyZyA9PT0gJy0tbmFtZScgJiYgYXJndltpICsgMV0pIG9wdHMubmFtZSA9IGFyZ3ZbKytpXTsKICAgIGVsc2UgaWYgKGFyZyA9PT0gJy0tZHVyYXRpb24nICYmIGFyZ3ZbaSArIDFdKSBvcHRzLmR1cmF0aW9uID0gTnVtYmVyKGFyZ3ZbKytpXSkgfHwgMzA7CiAgICBlbHNlIGlmIChhcmcgPT09ICctLXNlc3Npb24nICYmIGFyZ3ZbaSArIDFdKSBvcHRzLnNlc3Npb24gPSBhcmd2WysraV07CiAgICBlbHNlIGlmIChhcmcgPT09ICctLXJlcXVlc3QnICYmIGFyZ3ZbaSArIDFdKSBvcHRzLnJlcXVlc3QgPSBhcmd2WysraV07CiAgfQogIHJldHVybiBvcHRzOwp9Cgpjb25zdCBERUZBVUxUX0FMTE9XRURfRE9NQUlOUyA9IFsKICAnKi5hbXJpdGEuZWR1JywKICAnYW1yaXRhLmVkdScsCiAgJyouYW1yaXRhdmlkeWFsYXlhbS5vcmcnLAogICdsb2NhbGhvc3QnLAogICcxMjcuMC4wLjEnCl07CgpmdW5jdGlvbiBpc0RvbWFpbkFsbG93ZWQoaG9zdG5hbWUsIGFsbG93ZWRQYXR0ZXJucyA9IERFRkFVTFRfQUxMT1dFRF9ET01BSU5TKSB7CiAgaWYgKCFob3N0bmFtZSkgcmV0dXJuIGZhbHNlOwogIGNvbnN0IGhvc3QgPSBob3N0bmFtZS50b0xvd2VyQ2FzZSgpLnRyaW0oKTsKICBmb3IgKGNvbnN0IHJhd1BhdHRlcm4gb2YgYWxsb3dlZFBhdHRlcm5zKSB7CiAgICBjb25zdCBwYXR0ZXJuID0gcmF3UGF0dGVybi50b0xvd2VyQ2FzZSgpLnRyaW0oKTsKICAgIGlmICghcGF0dGVybikgY29udGludWU7CiAgICBpZiAocGF0dGVybiA9PT0gaG9zdCkgcmV0dXJuIHRydWU7CiAgICBpZiAocGF0dGVybi5zdGFydHNXaXRoKCcqLicpKSB7CiAgICAgIGNvbnN0IHN1ZmZpeCA9IHBhdHRlcm4uc2xpY2UoMik7CiAgICAgIGlmIChob3N0ID09PSBzdWZmaXggfHwgaG9zdC5lbmRzV2l0aCgnLicgKyBzdWZmaXgpKSByZXR1cm4gdHJ1ZTsKICAgIH0gZWxzZSBpZiAoaG9zdC5lbmRzV2l0aCgnLicgKyBwYXR0ZXJuKSkgewogICAgICByZXR1cm4gdHJ1ZTsKICAgIH0KICB9CiAgcmV0dXJuIGZhbHNlOwp9CgovLyBSRkMgNjQ1NSBDbGllbnQtdG8tU2VydmVyIE1hc2tlZCBCaW5hcnkgV2ViU29ja2V0IEZyYW1lICh1c2luZyAweDAwMDAwMDAwIG1hc2sga2V5KQpmdW5jdGlvbiBlbmNvZGVDbGllbnRXc0ZyYW1lKGJ1ZmZlcikgewogIGNvbnN0IGxlbiA9IGJ1ZmZlci5sZW5ndGg7CiAgbGV0IGhlYWRlcjsKICBpZiAobGVuIDwgMTI2KSB7CiAgICBoZWFkZXIgPSBCdWZmZXIuYWxsb2MoNik7CiAgICBoZWFkZXJbMF0gPSAweDgyOyAvLyBGSU4gKyBCaW5hcnkKICAgIGhlYWRlclsxXSA9IDB4ODAgfCBsZW47IC8vIE1hc2tlZAogIH0gZWxzZSBpZiAobGVuIDwgNjU1MzYpIHsKICAgIGhlYWRlciA9IEJ1ZmZlci5hbGxvYyg4KTsKICAgIGhlYWRlclswXSA9IDB4ODI7CiAgICBoZWFkZXJbMV0gPSAweDgwIHwgMTI2OwogICAgaGVhZGVyLndyaXRlVUludDE2QkUobGVuLCAyKTsKICB9IGVsc2UgewogICAgaGVhZGVyID0gQnVmZmVyLmFsbG9jKDE0KTsKICAgIGhlYWRlclswXSA9IDB4ODI7CiAgICBoZWFkZXJbMV0gPSAweDgwIHwgMTI3OwogICAgaGVhZGVyLndyaXRlQmlnVUludDY0QkUoQmlnSW50KGxlbiksIDIpOwogIH0KICByZXR1cm4gQnVmZmVyLmNvbmNhdChbaGVhZGVyLCBidWZmZXJdKTsKfQoKZnVuY3Rpb24gc2VuZFR1bm5lbEZyYW1lKHNvY2tldCwgaGVhZGVyT2JqLCBwYXlsb2FkQnVmID0gbnVsbCkgewogIGlmICghc29ja2V0IHx8IHNvY2tldC5kZXN0cm95ZWQpIHJldHVybiBmYWxzZTsKICBjb25zdCBkYXRhQnVmID0gcGF5bG9hZEJ1ZiA/IEJ1ZmZlci5mcm9tKHBheWxvYWRCdWYpIDogQnVmZmVyLmFsbG9jKDApOwogIGhlYWRlck9iai5sZW4gPSBkYXRhQnVmLmxlbmd0aDsKICBjb25zdCBoZWFkZXJCdWYgPSBCdWZmZXIuZnJvbShKU09OLnN0cmluZ2lmeShoZWFkZXJPYmopLCAndXRmOCcpOwogIGNvbnN0IGxlblByZWZpeCA9IEJ1ZmZlci5hbGxvYyg0KTsKICBsZW5QcmVmaXgud3JpdGVVSW50MzJCRShoZWFkZXJCdWYubGVuZ3RoLCAwKTsKICBjb25zdCBpbm5lciA9IEJ1ZmZlci5jb25jYXQoW2xlblByZWZpeCwgaGVhZGVyQnVmLCBkYXRhQnVmXSk7CiAgdHJ5IHsKICAgIHNvY2tldC53cml0ZShlbmNvZGVDbGllbnRXc0ZyYW1lKGlubmVyKSk7CiAgICByZXR1cm4gdHJ1ZTsKICB9IGNhdGNoIChfKSB7CiAgICByZXR1cm4gZmFsc2U7CiAgfQp9CgpmdW5jdGlvbiBhdHRhY2hGcmFtZVBhcnNlcihzb2NrZXQsIG9uRnJhbWUpIHsKICBsZXQgd3NCdWYgPSBCdWZmZXIuYWxsb2MoMCk7CiAgbGV0IGlubmVyQnVmID0gQnVmZmVyLmFsbG9jKDApOwoKICBmdW5jdGlvbiBwcm9jZXNzSW5uZXJGcmFtZXMoKSB7CiAgICB3aGlsZSAoaW5uZXJCdWYubGVuZ3RoID49IDQpIHsKICAgICAgY29uc3QgaGVhZGVyTGVuID0gaW5uZXJCdWYucmVhZFVJbnQzMkJFKDApOwogICAgICBpZiAoaGVhZGVyTGVuID4gMTAyNCAqIDEwMjQpIHsKICAgICAgICBzb2NrZXQuZGVzdHJveSgpOwogICAgICAgIHJldHVybjsKICAgICAgfQogICAgICBpZiAoaW5uZXJCdWYubGVuZ3RoIDwgNCArIGhlYWRlckxlbikgYnJlYWs7CiAgICAgIGxldCBoZWFkZXI7CiAgICAgIHRyeSB7CiAgICAgICAgaGVhZGVyID0gSlNPTi5wYXJzZShpbm5lckJ1Zi5zdWJhcnJheSg0LCA0ICsgaGVhZGVyTGVuKS50b1N0cmluZygndXRmOCcpKTsKICAgICAgfSBjYXRjaCAoXykgewogICAgICAgIHNvY2tldC5kZXN0cm95KCk7CiAgICAgICAgcmV0dXJuOwogICAgICB9CiAgICAgIGNvbnN0IHBheWxvYWRMZW4gPSBoZWFkZXIubGVuIHx8IDA7CiAgICAgIGlmIChpbm5lckJ1Zi5sZW5ndGggPCA0ICsgaGVhZGVyTGVuICsgcGF5bG9hZExlbikgYnJlYWs7CiAgICAgIGNvbnN0IHBheWxvYWQgPQogICAgICAgIHBheWxvYWRMZW4gPiAwCiAgICAgICAgICA/IGlubmVyQnVmLnN1YmFycmF5KDQgKyBoZWFkZXJMZW4sIDQgKyBoZWFkZXJMZW4gKyBwYXlsb2FkTGVuKQogICAgICAgICAgOiBCdWZmZXIuYWxsb2MoMCk7CiAgICAgIGlubmVyQnVmID0gaW5uZXJCdWYuc3ViYXJyYXkoNCArIGhlYWRlckxlbiArIHBheWxvYWRMZW4pOwogICAgICBvbkZyYW1lKGhlYWRlciwgcGF5bG9hZCk7CiAgICB9CiAgfQoKICBzb2NrZXQub24oJ2RhdGEnLCAoY2h1bmspID0+IHsKICAgIHdzQnVmID0gQnVmZmVyLmNvbmNhdChbd3NCdWYsIGNodW5rXSk7CiAgICB3aGlsZSAod3NCdWYubGVuZ3RoID49IDIpIHsKICAgICAgY29uc3QgZmlyc3RCeXRlID0gd3NCdWZbMF07CiAgICAgIGNvbnN0IHNlY29uZEJ5dGUgPSB3c0J1ZlsxXTsKICAgICAgY29uc3Qgb3Bjb2RlID0gZmlyc3RCeXRlICYgMHgwZjsKICAgICAgY29uc3QgaXNNYXNrZWQgPSBCb29sZWFuKHNlY29uZEJ5dGUgJiAweDgwKTsKICAgICAgbGV0IHBheWxvYWRMZW4gPSBzZWNvbmRCeXRlICYgMHg3ZjsKICAgICAgbGV0IG9mZnNldCA9IDI7CgogICAgICBpZiAocGF5bG9hZExlbiA9PT0gMTI2KSB7CiAgICAgICAgaWYgKHdzQnVmLmxlbmd0aCA8IDQpIHJldHVybjsKICAgICAgICBwYXlsb2FkTGVuID0gd3NCdWYucmVhZFVJbnQxNkJFKDIpOwogICAgICAgIG9mZnNldCA9IDQ7CiAgICAgIH0gZWxzZSBpZiAocGF5bG9hZExlbiA9PT0gMTI3KSB7CiAgICAgICAgaWYgKHdzQnVmLmxlbmd0aCA8IDEwKSByZXR1cm47CiAgICAgICAgcGF5bG9hZExlbiA9IE51bWJlcih3c0J1Zi5yZWFkQmlnVUludDY0QkUoMikpOwogICAgICAgIG9mZnNldCA9IDEwOwogICAgICB9CgogICAgICBjb25zdCBtYXNrTGVuID0gaXNNYXNrZWQgPyA0IDogMDsKICAgICAgaWYgKHdzQnVmLmxlbmd0aCA8IG9mZnNldCArIG1hc2tMZW4gKyBwYXlsb2FkTGVuKSByZXR1cm47CgogICAgICBsZXQgZnJhbWVEYXRhID0gd3NCdWYuc3ViYXJyYXkob2Zmc2V0ICsgbWFza0xlbiwgb2Zmc2V0ICsgbWFza0xlbiArIHBheWxvYWRMZW4pOwogICAgICBpZiAoaXNNYXNrZWQpIHsKICAgICAgICBjb25zdCBtYXNrS2V5ID0gd3NCdWYuc3ViYXJyYXkob2Zmc2V0LCBvZmZzZXQgKyA0KTsKICAgICAgICBjb25zdCB1bm1hc2tlZCA9IEJ1ZmZlci5hbGxvY1Vuc2FmZShmcmFtZURhdGEubGVuZ3RoKTsKICAgICAgICBmb3IgKGxldCBpID0gMDsgaSA8IGZyYW1lRGF0YS5sZW5ndGg7IGkrKykgewogICAgICAgICAgdW5tYXNrZWRbaV0gPSBmcmFtZURhdGFbaV0gXiBtYXNrS2V5W2kgJiAzXTsKICAgICAgICB9CiAgICAgICAgZnJhbWVEYXRhID0gdW5tYXNrZWQ7CiAgICAgIH0KCiAgICAgIHdzQnVmID0gd3NCdWYuc3ViYXJyYXkob2Zmc2V0ICsgbWFza0xlbiArIHBheWxvYWRMZW4pOwoKICAgICAgaWYgKG9wY29kZSA9PT0gMHgwOCkgewogICAgICAgIHNvY2tldC5kZXN0cm95KCk7CiAgICAgICAgcmV0dXJuOwogICAgICB9IGVsc2UgaWYgKG9wY29kZSA9PT0gMHgwMSB8fCBvcGNvZGUgPT09IDB4MDIgfHwgb3Bjb2RlID09PSAweDAwKSB7CiAgICAgICAgaW5uZXJCdWYgPSBCdWZmZXIuY29uY2F0KFtpbm5lckJ1ZiwgZnJhbWVEYXRhXSk7CiAgICAgICAgcHJvY2Vzc0lubmVyRnJhbWVzKCk7CiAgICAgIH0KICAgIH0KICB9KTsKfQoKZnVuY3Rpb24gc3RhcnRIb3N0ZWxsZXJBZ2VudChvcHRpb25zID0ge30pIHsKICBjb25zdCBjbGlPcHRzID0geyAuLi5wYXJzZUFyZ3MocHJvY2Vzcy5hcmd2KSwgLi4ub3B0aW9ucyB9OwogIGNvbnN0IHNlcnZlclVybCA9IG5ldyBVUkwoY2xpT3B0cy5zZXJ2ZXIpOwogIGNvbnN0IGlzVGxzID0gc2VydmVyVXJsLnByb3RvY29sID09PSAnaHR0cHM6JyB8fCBzZXJ2ZXJVcmwucHJvdG9jb2wgPT09ICd3c3M6JzsKICBjb25zdCBodHRwTW9kdWxlID0gaXNUbHMgPyBodHRwcyA6IGh0dHA7CgogIGNvbnN0IHR1bm5lbFBhdGggPQogICAgYC90dW5uZWw/aG9zdGVsbGVyTmFtZT0ke2VuY29kZVVSSUNvbXBvbmVudChjbGlPcHRzLm5hbWUpfWAgKwogICAgYCZkdXJhdGlvbj0ke2VuY29kZVVSSUNvbXBvbmVudChjbGlPcHRzLmR1cmF0aW9uKX1gICsKICAgIChjbGlPcHRzLnNlc3Npb24gPyBgJnNlc3Npb25Db2RlPSR7ZW5jb2RlVVJJQ29tcG9uZW50KGNsaU9wdHMuc2Vzc2lvbil9YCA6ICcnKSArCiAgICAoY2xpT3B0cy5yZXF1ZXN0ID8gYCZyZXF1ZXN0SWQ9JHtlbmNvZGVVUklDb21wb25lbnQoY2xpT3B0cy5yZXF1ZXN0KX1gIDogJycpOwoKICBjb25zb2xlLmxvZygnPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0nKTsKICBjb25zb2xlLmxvZygnIPCfj6AgQU1SSVRBIENBTVBVUyBCUklER0Ug4oCUIEhPU1RFTExFUiBSRUxBWSBBR0VOVCcpOwogIGNvbnNvbGUubG9nKCc9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PScpOwogIGNvbnNvbGUubG9nKGAg8J+ToSBDb25uZWN0aW5nIHRvIENsb3VkIEJyb2tlciAgOiAke2NsaU9wdHMuc2VydmVyfWApOwogIGNvbnNvbGUubG9nKGAg8J+RpCBIb3N0ZWxsZXIgSWRlbnRpZmllciAgICAgICAgOiAke2NsaU9wdHMubmFtZX1gKTsKICBjb25zb2xlLmxvZyhgIOKPse+4jyAgQXBwcm92ZWQgRHVyYXRpb24gICAgICAgICAgOiAke2NsaU9wdHMuZHVyYXRpb259IG1pbnV0ZXNgKTsKICBjb25zb2xlLmxvZygnPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0nKTsKCiAgY29uc3Qgd3NLZXkgPSBjcnlwdG8ucmFuZG9tQnl0ZXMoMTYpLnRvU3RyaW5nKCdiYXNlNjQnKTsKCiAgY29uc3QgcmVxID0gaHR0cE1vZHVsZS5yZXF1ZXN0KHsKICAgIGhvc3RuYW1lOiBzZXJ2ZXJVcmwuaG9zdG5hbWUsCiAgICBwb3J0OiBzZXJ2ZXJVcmwucG9ydCB8fCAoaXNUbHMgPyA0NDMgOiA4MCksCiAgICBwYXRoOiB0dW5uZWxQYXRoLAogICAgbWV0aG9kOiAnR0VUJywKICAgIGhlYWRlcnM6IHsKICAgICAgSG9zdDogc2VydmVyVXJsLmhvc3QsCiAgICAgIENvbm5lY3Rpb246ICdVcGdyYWRlJywKICAgICAgVXBncmFkZTogJ3dlYnNvY2tldCcsCiAgICAgICdTZWMtV2ViU29ja2V0LVZlcnNpb24nOiAnMTMnLAogICAgICAnU2VjLVdlYlNvY2tldC1LZXknOiB3c0tleQogICAgfQogIH0pOwoKICBjb25zdCBhY3RpdmVTb2NrZXRzID0gbmV3IE1hcCgpOwogIGxldCBhbGxvd2VkRG9tYWlucyA9IFsuLi5ERUZBVUxUX0FMTE9XRURfRE9NQUlOU107CiAgbGV0IGV4cGlyeVRpbWVyID0gbnVsbDsKICBsZXQgcGluZ0ludGVydmFsID0gbnVsbDsKCiAgcmVxLm9uKCd1cGdyYWRlJywgKHJlcywgdHVubmVsU29ja2V0LCBoZWFkKSA9PiB7CiAgICB0dW5uZWxTb2NrZXQuc2V0Tm9EZWxheSh0cnVlKTsKICAgIGNvbnN0IGFzc2lnbmVkQ29kZSA9IHJlcy5oZWFkZXJzWyd4LXNlc3Npb24tY29kZSddIHx8IGNsaU9wdHMuc2Vzc2lvbiB8fCAnQUNUSVZFJzsKCiAgICBjb25zb2xlLmxvZyhgIOKchSBDQU1QVVMgUkVMQVkgTElWRSEgU2Vzc2lvbiBDb2RlOiBbICR7YXNzaWduZWRDb2RlfSBdYCk7CiAgICBjb25zb2xlLmxvZygnIPCflJIgRGF5IFNjaG9sYXJzIGNhbiBub3cgZW50ZXIgdGhpcyBjb2RlIGluIHRoZSBBbXJpdGEgVlBOIENocm9tZSBFeHRlbnNpb24uJyk7CiAgICBjb25zb2xlLmxvZygnIPCfm6HvuI8gIEhvc3RlbGxlciBEb21haW4gR3VhcmQgQWN0aXZlOiBPbmx5ICouYW1yaXRhLmVkdSB0cmFmZmljIGlzIGFsbG93ZWQuJyk7CiAgICBjb25zb2xlLmxvZygnIPCfm5EgUHJlc3MgQ3RybCtDIGFueXRpbWUgdG8gaW1tZWRpYXRlbHkgc3RvcCBzaGFyaW5nIHlvdXIgY29ubmVjdGlvbi5cbicpOwoKICAgIHBpbmdJbnRlcnZhbCA9IHNldEludGVydmFsKCgpID0+IHsKICAgICAgc2VuZFR1bm5lbEZyYW1lKHR1bm5lbFNvY2tldCwgeyB0eXBlOiAnUElORycsIHRzOiBEYXRlLm5vdygpIH0pOwogICAgfSwgMTUwMDApLnVucmVmKCk7CgogICAgYXR0YWNoRnJhbWVQYXJzZXIodHVubmVsU29ja2V0LCAoaGVhZGVyLCBwYXlsb2FkKSA9PiB7CiAgICAgIGlmIChoZWFkZXIudHlwZSA9PT0gJ1dFTENPTUUnKSB7CiAgICAgICAgaWYgKGhlYWRlci5zZXNzaW9uQ29kZSkgewogICAgICAgICAgY29uc29sZS5sb2coYCDwn46rIENvbmZpcm1lZCBTZXNzaW9uIENvZGU6IFsgJHtoZWFkZXIuc2Vzc2lvbkNvZGV9IF1gKTsKICAgICAgICB9CiAgICAgICAgaWYgKEFycmF5LmlzQXJyYXkoaGVhZGVyLmFsbG93ZWREb21haW5zKSkgewogICAgICAgICAgYWxsb3dlZERvbWFpbnMgPSBBcnJheS5mcm9tKG5ldyBTZXQoWy4uLmhlYWRlci5hbGxvd2VkRG9tYWlucywgLi4uREVGQVVMVF9BTExPV0VEX0RPTUFJTlNdKSk7CiAgICAgICAgfQogICAgICAgIGNvbnN0IHJlbWFpbmluZ01zID0gTWF0aC5tYXgoMTAwMCwgKGhlYWRlci5leHBpcmVzQXQgfHwgRGF0ZS5ub3coKSArIGNsaU9wdHMuZHVyYXRpb24gKiA2MDAwMCkgLSBEYXRlLm5vdygpKTsKICAgICAgICBpZiAoZXhwaXJ5VGltZXIpIGNsZWFyVGltZW91dChleHBpcnlUaW1lcik7CiAgICAgICAgZXhwaXJ5VGltZXIgPSBzZXRUaW1lb3V0KCgpID0+IHsKICAgICAgICAgIGNvbnNvbGUubG9nKCdcbiDij7AgQXBwcm92ZWQgc2Vzc2lvbiBkdXJhdGlvbiByZWFjaGVkLiBDbG9zaW5nIENhbXB1cyBSZWxheSBhdXRvbWF0aWNhbGx5Li4uJyk7CiAgICAgICAgICB0dW5uZWxTb2NrZXQuZGVzdHJveSgpOwogICAgICAgICAgcHJvY2Vzcy5leGl0KDApOwogICAgICAgIH0sIHJlbWFpbmluZ01zKTsKICAgICAgICBleHBpcnlUaW1lci51bnJlZigpOwogICAgICB9IGVsc2UgaWYgKGhlYWRlci50eXBlID09PSAnU0VTU0lPTl9FTkQnKSB7CiAgICAgICAgY29uc29sZS5sb2coYFxuIPCfm5EgU2Vzc2lvbiBlbmRlZCBieSBwbGF0Zm9ybTogJHtoZWFkZXIucmVhc29uIHx8ICdDbG9zZWQnfWApOwogICAgICAgIGZvciAoY29uc3QgWywgc10gb2YgYWN0aXZlU29ja2V0cykgcy5kZXN0cm95KCk7CiAgICAgICAgYWN0aXZlU29ja2V0cy5jbGVhcigpOwogICAgICAgIHR1bm5lbFNvY2tldC5kZXN0cm95KCk7CiAgICAgICAgaWYgKHJlcXVpcmUubWFpbiA9PT0gbW9kdWxlKSBwcm9jZXNzLmV4aXQoMCk7CiAgICAgIH0gZWxzZSBpZiAoaGVhZGVyLnR5cGUgPT09ICdDT05ORUNUJykgewogICAgICAgIGNvbnN0IHsgc3RyZWFtSWQsIGhvc3QsIHBvcnQsIGVuY3J5cHRlZCB9ID0gaGVhZGVyOwoKICAgICAgICBpZiAoIWlzRG9tYWluQWxsb3dlZChob3N0LCBhbGxvd2VkRG9tYWlucykpIHsKICAgICAgICAgIGNvbnNvbGUubG9nKGAg8J+aqyBbQkxPQ0tFRF0gUmVqZWN0ZWQgbm9uLWNvbGxlZ2UgdGFyZ2V0OiAke2hvc3R9OiR7cG9ydH1gKTsKICAgICAgICAgIHNlbmRUdW5uZWxGcmFtZSh0dW5uZWxTb2NrZXQsIHsKICAgICAgICAgICAgdHlwZTogJ0NPTk5FQ1RfRVJSJywKICAgICAgICAgICAgc3RyZWFtSWQsCiAgICAgICAgICAgIGVycm9yOiBgSG9zdGVsbGVyIHBvbGljeSBibG9ja2VkIG5vbi1jb2xsZWdlIGRvbWFpbjogJHtob3N0fWAKICAgICAgICAgIH0pOwogICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgY29uc29sZS5sb2coYCDwn5SXIFtSRUxBWSAjJHtzdHJlYW1JZH1dICR7ZW5jcnlwdGVkID8gJ/CflJIgSFRUUFMgVHVubmVsJyA6ICfwn4yQIEhUVFAnfSAtPiAke2hvc3R9OiR7cG9ydH1gKTsKCiAgICAgICAgY29uc3QgdGFyZ2V0U29ja2V0ID0gbmV0LmNvbm5lY3QoeyBob3N0LCBwb3J0OiBOdW1iZXIocG9ydCkgfHwgNDQzIH0sICgpID0+IHsKICAgICAgICAgIHRhcmdldFNvY2tldC5zZXROb0RlbGF5KHRydWUpOwogICAgICAgICAgc2VuZFR1bm5lbEZyYW1lKHR1bm5lbFNvY2tldCwgeyB0eXBlOiAnQ09OTkVDVF9PSycsIHN0cmVhbUlkIH0pOwogICAgICAgIH0pOwoKICAgICAgICBhY3RpdmVTb2NrZXRzLnNldChzdHJlYW1JZCwgdGFyZ2V0U29ja2V0KTsKCiAgICAgICAgdGFyZ2V0U29ja2V0Lm9uKCdkYXRhJywgKGNodW5rKSA9PiB7CiAgICAgICAgICBzZW5kVHVubmVsRnJhbWUodHVubmVsU29ja2V0LCB7IHR5cGU6ICdEQVRBJywgc3RyZWFtSWQgfSwgY2h1bmspOwogICAgICAgIH0pOwoKICAgICAgICB0YXJnZXRTb2NrZXQub24oJ2VuZCcsICgpID0+IHsKICAgICAgICAgIHNlbmRUdW5uZWxGcmFtZSh0dW5uZWxTb2NrZXQsIHsgdHlwZTogJ0VORCcsIHN0cmVhbUlkIH0pOwogICAgICAgICAgYWN0aXZlU29ja2V0cy5kZWxldGUoc3RyZWFtSWQpOwogICAgICAgIH0pOwoKICAgICAgICB0YXJnZXRTb2NrZXQub24oJ2Vycm9yJywgKGVycikgPT4gewogICAgICAgICAgc2VuZFR1bm5lbEZyYW1lKHR1bm5lbFNvY2tldCwgewogICAgICAgICAgICB0eXBlOiAnQ09OTkVDVF9FUlInLAogICAgICAgICAgICBzdHJlYW1JZCwKICAgICAgICAgICAgZXJyb3I6IGVyci5tZXNzYWdlCiAgICAgICAgICB9KTsKICAgICAgICAgIGFjdGl2ZVNvY2tldHMuZGVsZXRlKHN0cmVhbUlkKTsKICAgICAgICB9KTsKCiAgICAgICAgdGFyZ2V0U29ja2V0Lm9uKCdjbG9zZScsICgpID0+IHsKICAgICAgICAgIGFjdGl2ZVNvY2tldHMuZGVsZXRlKHN0cmVhbUlkKTsKICAgICAgICB9KTsKICAgICAgfSBlbHNlIGlmIChoZWFkZXIudHlwZSA9PT0gJ0RBVEEnKSB7CiAgICAgICAgY29uc3QgdGFyZ2V0U29ja2V0ID0gYWN0aXZlU29ja2V0cy5nZXQoaGVhZGVyLnN0cmVhbUlkKTsKICAgICAgICBpZiAodGFyZ2V0U29ja2V0ICYmICF0YXJnZXRTb2NrZXQuZGVzdHJveWVkICYmIHBheWxvYWQubGVuZ3RoID4gMCkgewogICAgICAgICAgdHJ5IHsKICAgICAgICAgICAgdGFyZ2V0U29ja2V0LndyaXRlKHBheWxvYWQpOwogICAgICAgICAgfSBjYXRjaCAoXykge30KICAgICAgICB9CiAgICAgIH0gZWxzZSBpZiAoaGVhZGVyLnR5cGUgPT09ICdFTkQnKSB7CiAgICAgICAgY29uc3QgdGFyZ2V0U29ja2V0ID0gYWN0aXZlU29ja2V0cy5nZXQoaGVhZGVyLnN0cmVhbUlkKTsKICAgICAgICBpZiAodGFyZ2V0U29ja2V0KSB7CiAgICAgICAgICB0cnkgewogICAgICAgICAgICB0YXJnZXRTb2NrZXQuZW5kKCk7CiAgICAgICAgICB9IGNhdGNoIChfKSB7fQogICAgICAgICAgYWN0aXZlU29ja2V0cy5kZWxldGUoaGVhZGVyLnN0cmVhbUlkKTsKICAgICAgICB9CiAgICAgIH0KICAgIH0pOwoKICAgIGlmIChoZWFkICYmIGhlYWQubGVuZ3RoID4gMCkgewogICAgICB0dW5uZWxTb2NrZXQuZW1pdCgnZGF0YScsIGhlYWQpOwogICAgfQoKICAgIHR1bm5lbFNvY2tldC5vbignY2xvc2UnLCAoKSA9PiB7CiAgICAgIGlmIChwaW5nSW50ZXJ2YWwpIGNsZWFySW50ZXJ2YWwocGluZ0ludGVydmFsKTsKICAgICAgZm9yIChjb25zdCBbLCBzXSBvZiBhY3RpdmVTb2NrZXRzKSBzLmRlc3Ryb3koKTsKICAgICAgYWN0aXZlU29ja2V0cy5jbGVhcigpOwogICAgICBjb25zb2xlLmxvZygnIPCflIwgRGlzY29ubmVjdGVkIGZyb20gQnJva2VyIFNlcnZlci4nKTsKICAgIH0pOwogIH0pOwoKICByZXEub24oJ3Jlc3BvbnNlJywgKHJlcykgPT4gewogICAgY29uc29sZS5lcnJvcihgIOKdjCBTZXJ2ZXIgcmVzcG9uZGVkIHdpdGggSFRUUCAke3Jlcy5zdGF0dXNDb2RlfSBpbnN0ZWFkIG9mIFdlYlNvY2tldCBVcGdyYWRlLmApOwogIH0pOwoKICByZXEub24oJ2Vycm9yJywgKGVycikgPT4gewogICAgY29uc29sZS5lcnJvcihgIOKdjCBGYWlsZWQgdG8gY29ubmVjdCB0byBCcm9rZXIgU2VydmVyICgke2NsaU9wdHMuc2VydmVyfSk6ICR7ZXJyLm1lc3NhZ2V9YCk7CiAgfSk7CgogIHJlcS5lbmQoKTsKICByZXR1cm4gcmVxOwp9CgppZiAocmVxdWlyZS5tYWluID09PSBtb2R1bGUpIHsKICBzdGFydEhvc3RlbGxlckFnZW50KCk7Cn0KCm1vZHVsZS5leHBvcnRzID0geyBzdGFydEhvc3RlbGxlckFnZW50IH07Cg==";
        private const string BRIDGE_B64 = "LyoqCiAqIEFtcml0YSBDYW1wdXMgQnJpZGdlIOKAlCBEYXkgU2Nob2xhciBDbG91ZCBCcmlkZ2UgKDEyNy4wLjAuMTo4MDgwIC0+IFJlbmRlciBDbG91ZCkKICoKICogV2h5IHRoaXMgZXhpc3RzOgogKiBSZW5kZXIgb25seSBleHBvc2VzIEhUVFBTL1dlYlNvY2tldHMgb24gcG9ydCA0NDMgKGh0dHBzOi8vYW1yaXRhLWNhbXB1cy12cG4ub25yZW5kZXIuY29tKSwKICogd2hpbGUgQ2hyb21lJ3MgcHJveHkgQVBJIGV4cGVjdHMgYSBsb2NhbCBIVFRQL0hUVFBTIENPTk5FQ1QgcHJveHkgcG9ydCAoMTI3LjAuMC4xOjgwODApLgogKgogKiBUaGlzIGxpZ2h0d2VpZ2h0IGxvY2FsIGJyaWRnZSBydW5zIG9uIHRoZSBEYXkgU2Nob2xhcidzIGxhcHRvcCAoMTI3LjAuMC4xOjgwODApIGFuZAogKiB0dW5uZWxzIGV2ZXJ5ICouYW1yaXRhLmVkdSBicm93c2VyIHJlcXVlc3Qgb3ZlciBhIHNlY3VyZSBXZWJTb2NrZXQgKHdzczovLykgdGhyb3VnaAogKiBodHRwczovL2Ftcml0YS1jYW1wdXMtdnBuLm9ucmVuZGVyLmNvbSBkaXJlY3RseSB0byB0aGUgSG9zdGVsbGVyJ3MgbGFwdG9wIG9uIENvbGxlZ2UgV2ktRmkhCiAqLwoKY29uc3QgaHR0cCA9IHJlcXVpcmUoJ2h0dHAnKTsKY29uc3QgaHR0cHMgPSByZXF1aXJlKCdodHRwcycpOwpjb25zdCBjcnlwdG8gPSByZXF1aXJlKCdjcnlwdG8nKTsKY29uc3QgeyBVUkwgfSA9IHJlcXVpcmUoJ3VybCcpOwoKY29uc3QgQ0xPVURfU0VSVkVSID0gcHJvY2Vzcy5lbnYuQU1SSVRBX0JST0tFUl9VUkwgfHwgJ2h0dHBzOi8vYW1yaXRhLWNhbXB1cy12cG4ub25yZW5kZXIuY29tJzsKY29uc3QgTE9DQUxfUFJPWFlfUE9SVCA9IE51bWJlcihwcm9jZXNzLmVudi5QUk9YWV9QT1JUIHx8IDgwODApOwoKZnVuY3Rpb24gZW5jb2RlQ2xpZW50V3NGcmFtZShidWZmZXIpIHsKICBjb25zdCBsZW4gPSBidWZmZXIubGVuZ3RoOwogIGxldCBoZWFkZXI7CiAgaWYgKGxlbiA8IDEyNikgewogICAgaGVhZGVyID0gQnVmZmVyLmFsbG9jKDYpOwogICAgaGVhZGVyWzBdID0gMHg4MjsKICAgIGhlYWRlclsxXSA9IDB4ODAgfCBsZW47CiAgfSBlbHNlIGlmIChsZW4gPCA2NTUzNikgewogICAgaGVhZGVyID0gQnVmZmVyLmFsbG9jKDgpOwogICAgaGVhZGVyWzBdID0gMHg4MjsKICAgIGhlYWRlclsxXSA9IDB4ODAgfCAxMjY7CiAgICBoZWFkZXIud3JpdGVVSW50MTZCRShsZW4sIDIpOwogIH0gZWxzZSB7CiAgICBoZWFkZXIgPSBCdWZmZXIuYWxsb2MoMTQpOwogICAgaGVhZGVyWzBdID0gMHg4MjsKICAgIGhlYWRlclsxXSA9IDB4ODAgfCAxMjc7CiAgICBoZWFkZXIud3JpdGVCaWdVSW50NjRCRShCaWdJbnQobGVuKSwgMik7CiAgfQogIHJldHVybiBCdWZmZXIuY29uY2F0KFtoZWFkZXIsIGJ1ZmZlcl0pOwp9CgpmdW5jdGlvbiBzZW5kVHVubmVsRnJhbWUoc29ja2V0LCBoZWFkZXJPYmosIHBheWxvYWRCdWYgPSBudWxsKSB7CiAgaWYgKCFzb2NrZXQgfHwgc29ja2V0LmRlc3Ryb3llZCkgcmV0dXJuIGZhbHNlOwogIGNvbnN0IGRhdGFCdWYgPSBwYXlsb2FkQnVmID8gQnVmZmVyLmZyb20ocGF5bG9hZEJ1ZikgOiBCdWZmZXIuYWxsb2MoMCk7CiAgaGVhZGVyT2JqLmxlbiA9IGRhdGFCdWYubGVuZ3RoOwogIGNvbnN0IGhlYWRlckJ1ZiA9IEJ1ZmZlci5mcm9tKEpTT04uc3RyaW5naWZ5KGhlYWRlck9iaiksICd1dGY4Jyk7CiAgY29uc3QgbGVuUHJlZml4ID0gQnVmZmVyLmFsbG9jKDQpOwogIGxlblByZWZpeC53cml0ZVVJbnQzMkJFKGhlYWRlckJ1Zi5sZW5ndGgsIDApOwogIGNvbnN0IGlubmVyID0gQnVmZmVyLmNvbmNhdChbbGVuUHJlZml4LCBoZWFkZXJCdWYsIGRhdGFCdWZdKTsKICB0cnkgewogICAgc29ja2V0LndyaXRlKGVuY29kZUNsaWVudFdzRnJhbWUoaW5uZXIpKTsKICAgIHJldHVybiB0cnVlOwogIH0gY2F0Y2ggKF8pIHsKICAgIHJldHVybiBmYWxzZTsKICB9Cn0KCmZ1bmN0aW9uIGF0dGFjaEZyYW1lUGFyc2VyKHNvY2tldCwgb25GcmFtZSkgewogIGxldCB3c0J1ZiA9IEJ1ZmZlci5hbGxvYygwKTsKICBsZXQgaW5uZXJCdWYgPSBCdWZmZXIuYWxsb2MoMCk7CgogIGZ1bmN0aW9uIHByb2Nlc3NJbm5lckZyYW1lcygpIHsKICAgIHdoaWxlIChpbm5lckJ1Zi5sZW5ndGggPj0gNCkgewogICAgICBjb25zdCBoZWFkZXJMZW4gPSBpbm5lckJ1Zi5yZWFkVUludDMyQkUoMCk7CiAgICAgIGlmIChoZWFkZXJMZW4gPiAxMDI0ICogMTAyNCkgewogICAgICAgIHNvY2tldC5kZXN0cm95KCk7CiAgICAgICAgcmV0dXJuOwogICAgICB9CiAgICAgIGlmIChpbm5lckJ1Zi5sZW5ndGggPCA0ICsgaGVhZGVyTGVuKSBicmVhazsKICAgICAgbGV0IGhlYWRlcjsKICAgICAgdHJ5IHsKICAgICAgICBoZWFkZXIgPSBKU09OLnBhcnNlKGlubmVyQnVmLnN1YmFycmF5KDQsIDQgKyBoZWFkZXJMZW4pLnRvU3RyaW5nKCd1dGY4JykpOwogICAgICB9IGNhdGNoIChfKSB7CiAgICAgICAgc29ja2V0LmRlc3Ryb3koKTsKICAgICAgICByZXR1cm47CiAgICAgIH0KICAgICAgY29uc3QgcGF5bG9hZExlbiA9IGhlYWRlci5sZW4gfHwgMDsKICAgICAgaWYgKGlubmVyQnVmLmxlbmd0aCA8IDQgKyBoZWFkZXJMZW4gKyBwYXlsb2FkTGVuKSBicmVhazsKICAgICAgY29uc3QgcGF5bG9hZCA9CiAgICAgICAgcGF5bG9hZExlbiA+IDAKICAgICAgICAgID8gaW5uZXJCdWYuc3ViYXJyYXkoNCArIGhlYWRlckxlbiwgNCArIGhlYWRlckxlbiArIHBheWxvYWRMZW4pCiAgICAgICAgICA6IEJ1ZmZlci5hbGxvYygwKTsKICAgICAgaW5uZXJCdWYgPSBpbm5lckJ1Zi5zdWJhcnJheSg0ICsgaGVhZGVyTGVuICsgcGF5bG9hZExlbik7CiAgICAgIG9uRnJhbWUoaGVhZGVyLCBwYXlsb2FkKTsKICAgIH0KICB9CgogIHNvY2tldC5vbignZGF0YScsIChjaHVuaykgPT4gewogICAgd3NCdWYgPSBCdWZmZXIuY29uY2F0KFt3c0J1ZiwgY2h1bmtdKTsKICAgIHdoaWxlICh3c0J1Zi5sZW5ndGggPj0gMikgewogICAgICBjb25zdCBmaXJzdEJ5dGUgPSB3c0J1ZlswXTsKICAgICAgY29uc3Qgc2Vjb25kQnl0ZSA9IHdzQnVmWzFdOwogICAgICBjb25zdCBvcGNvZGUgPSBmaXJzdEJ5dGUgJiAweDBmOwogICAgICBjb25zdCBpc01hc2tlZCA9IEJvb2xlYW4oc2Vjb25kQnl0ZSAmIDB4ODApOwogICAgICBsZXQgcGF5bG9hZExlbiA9IHNlY29uZEJ5dGUgJiAweDdmOwogICAgICBsZXQgb2Zmc2V0ID0gMjsKCiAgICAgIGlmIChwYXlsb2FkTGVuID09PSAxMjYpIHsKICAgICAgICBpZiAod3NCdWYubGVuZ3RoIDwgNCkgcmV0dXJuOwogICAgICAgIHBheWxvYWRMZW4gPSB3c0J1Zi5yZWFkVUludDE2QkUoMik7CiAgICAgICAgb2Zmc2V0ID0gNDsKICAgICAgfSBlbHNlIGlmIChwYXlsb2FkTGVuID09PSAxMjcpIHsKICAgICAgICBpZiAod3NCdWYubGVuZ3RoIDwgMTApIHJldHVybjsKICAgICAgICBwYXlsb2FkTGVuID0gTnVtYmVyKHdzQnVmLnJlYWRCaWdVSW50NjRCRSgyKSk7CiAgICAgICAgb2Zmc2V0ID0gMTA7CiAgICAgIH0KCiAgICAgIGNvbnN0IG1hc2tMZW4gPSBpc01hc2tlZCA/IDQgOiAwOwogICAgICBpZiAod3NCdWYubGVuZ3RoIDwgb2Zmc2V0ICsgbWFza0xlbiArIHBheWxvYWRMZW4pIHJldHVybjsKCiAgICAgIGxldCBmcmFtZURhdGEgPSB3c0J1Zi5zdWJhcnJheShvZmZzZXQgKyBtYXNrTGVuLCBvZmZzZXQgKyBtYXNrTGVuICsgcGF5bG9hZExlbik7CiAgICAgIGlmIChpc01hc2tlZCkgewogICAgICAgIGNvbnN0IG1hc2tLZXkgPSB3c0J1Zi5zdWJhcnJheShvZmZzZXQsIG9mZnNldCArIDQpOwogICAgICAgIGNvbnN0IHVubWFza2VkID0gQnVmZmVyLmFsbG9jVW5zYWZlKGZyYW1lRGF0YS5sZW5ndGgpOwogICAgICAgIGZvciAobGV0IGkgPSAwOyBpIDwgZnJhbWVEYXRhLmxlbmd0aDsgaSsrKSB7CiAgICAgICAgICB1bm1hc2tlZFtpXSA9IGZyYW1lRGF0YVtpXSBeIG1hc2tLZXlbaSAmIDNdOwogICAgICAgIH0KICAgICAgICBmcmFtZURhdGEgPSB1bm1hc2tlZDsKICAgICAgfQoKICAgICAgd3NCdWYgPSB3c0J1Zi5zdWJhcnJheShvZmZzZXQgKyBtYXNrTGVuICsgcGF5bG9hZExlbik7CgogICAgICBpZiAob3Bjb2RlID09PSAweDA4KSB7CiAgICAgICAgc29ja2V0LmRlc3Ryb3koKTsKICAgICAgICByZXR1cm47CiAgICAgIH0gZWxzZSBpZiAob3Bjb2RlID09PSAweDAxIHx8IG9wY29kZSA9PT0gMHgwMiB8fCBvcGNvZGUgPT09IDB4MDApIHsKICAgICAgICBpbm5lckJ1ZiA9IEJ1ZmZlci5jb25jYXQoW2lubmVyQnVmLCBmcmFtZURhdGFdKTsKICAgICAgICBwcm9jZXNzSW5uZXJGcmFtZXMoKTsKICAgICAgfQogICAgfQogIH0pOwp9CgpsZXQgY2xvdWRTb2NrZXQgPSBudWxsOwpjb25zdCBwZW5kaW5nU3RyZWFtcyA9IG5ldyBNYXAoKTsKbGV0IG5leHRTdHJlYW1JZCA9IDE7CgpmdW5jdGlvbiBjb25uZWN0VG9SZW5kZXJDbG91ZChzZXJ2ZXJCYXNlVXJsID0gQ0xPVURfU0VSVkVSKSB7CiAgY29uc3QgcGFyc2VkID0gbmV3IFVSTChzZXJ2ZXJCYXNlVXJsKTsKICBjb25zdCBpc1RscyA9IHBhcnNlZC5wcm90b2NvbCA9PT0gJ2h0dHBzOicgfHwgcGFyc2VkLnByb3RvY29sID09PSAnd3NzOic7CiAgY29uc3QgbW9kID0gaXNUbHMgPyBodHRwcyA6IGh0dHA7CgogIGNvbnN0IHJlcSA9IG1vZC5yZXF1ZXN0KHsKICAgIGhvc3RuYW1lOiBwYXJzZWQuaG9zdG5hbWUsCiAgICBwb3J0OiBwYXJzZWQucG9ydCB8fCAoaXNUbHMgPyA0NDMgOiA4MCksCiAgICBwYXRoOiAnL3NjaG9sYXInLAogICAgbWV0aG9kOiAnR0VUJywKICAgIGhlYWRlcnM6IHsKICAgICAgSG9zdDogcGFyc2VkLmhvc3QsCiAgICAgIENvbm5lY3Rpb246ICdVcGdyYWRlJywKICAgICAgVXBncmFkZTogJ3dlYnNvY2tldCcsCiAgICAgICdTZWMtV2ViU29ja2V0LVZlcnNpb24nOiAnMTMnLAogICAgICAnU2VjLVdlYlNvY2tldC1LZXknOiBjcnlwdG8ucmFuZG9tQnl0ZXMoMTYpLnRvU3RyaW5nKCdiYXNlNjQnKQogICAgfQogIH0pOwoKICByZXEub24oJ3VwZ3JhZGUnLCAocmVzLCBzb2NrZXQsIGhlYWQpID0+IHsKICAgIHNvY2tldC5zZXROb0RlbGF5KHRydWUpOwogICAgY2xvdWRTb2NrZXQgPSBzb2NrZXQ7CiAgICBjb25zb2xlLmxvZyhgIOKchSBDb25uZWN0ZWQgdG8gUmVuZGVyIENsb3VkIFNlcnZlciAoJHtzZXJ2ZXJCYXNlVXJsfSlgKTsKCiAgICBjb25zdCBwaW5nVGltZXIgPSBzZXRJbnRlcnZhbCgoKSA9PiB7CiAgICAgIHNlbmRUdW5uZWxGcmFtZShzb2NrZXQsIHsgdHlwZTogJ1BJTkcnIH0pOwogICAgfSwgMTUwMDApLnVucmVmKCk7CgogICAgYXR0YWNoRnJhbWVQYXJzZXIoc29ja2V0LCAoaGVhZGVyLCBwYXlsb2FkKSA9PiB7CiAgICAgIGNvbnN0IHN0cmVhbSA9IHBlbmRpbmdTdHJlYW1zLmdldChoZWFkZXIuc3RyZWFtSWQpOwogICAgICBpZiAoIXN0cmVhbSkgcmV0dXJuOwoKICAgICAgaWYgKGhlYWRlci50eXBlID09PSAnQ09OTkVDVF9PSycpIHsKICAgICAgICBpZiAoc3RyZWFtLm9uQ29ubmVjdGVkKSBzdHJlYW0ub25Db25uZWN0ZWQoKTsKICAgICAgfSBlbHNlIGlmIChoZWFkZXIudHlwZSA9PT0gJ0NPTk5FQ1RfRVJSJykgewogICAgICAgIGlmIChzdHJlYW0ub25FcnJvcikgc3RyZWFtLm9uRXJyb3IoaGVhZGVyLnN0YXR1cyB8fCA1MDIsIGhlYWRlci5lcnJvciB8fCAnUmVsYXkgZmFpbGVkJyk7CiAgICAgICAgcGVuZGluZ1N0cmVhbXMuZGVsZXRlKGhlYWRlci5zdHJlYW1JZCk7CiAgICAgIH0gZWxzZSBpZiAoaGVhZGVyLnR5cGUgPT09ICdEQVRBJykgewogICAgICAgIGlmIChwYXlsb2FkLmxlbmd0aCA+IDAgJiYgIXN0cmVhbS5zb2NrZXQuZGVzdHJveWVkKSB7CiAgICAgICAgICB0cnkgeyBzdHJlYW0uc29ja2V0LndyaXRlKHBheWxvYWQpOyB9IGNhdGNoIChfKSB7fQogICAgICAgIH0KICAgICAgfSBlbHNlIGlmIChoZWFkZXIudHlwZSA9PT0gJ0VORCcpIHsKICAgICAgICB0cnkgeyBzdHJlYW0uc29ja2V0LmVuZCgpOyB9IGNhdGNoIChfKSB7fQogICAgICAgIHBlbmRpbmdTdHJlYW1zLmRlbGV0ZShoZWFkZXIuc3RyZWFtSWQpOwogICAgICB9CiAgICB9KTsKCiAgICBpZiAoaGVhZCAmJiBoZWFkLmxlbmd0aCA+IDApIHNvY2tldC5lbWl0KCdkYXRhJywgaGVhZCk7CgogICAgc29ja2V0Lm9uKCdjbG9zZScsICgpID0+IHsKICAgICAgY2xlYXJJbnRlcnZhbChwaW5nVGltZXIpOwogICAgICBjbG91ZFNvY2tldCA9IG51bGw7CiAgICAgIGZvciAoY29uc3QgWywgc10gb2YgcGVuZGluZ1N0cmVhbXMpIHsKICAgICAgICB0cnkgeyBzLnNvY2tldC5kZXN0cm95KCk7IH0gY2F0Y2ggKF8pIHt9CiAgICAgIH0KICAgICAgcGVuZGluZ1N0cmVhbXMuY2xlYXIoKTsKICAgICAgY29uc29sZS5sb2coJyDwn5SEIENsb3VkIHNvY2tldCBjbG9zZWQuIFJlY29ubmVjdGluZyBpbiAycy4uLicpOwogICAgICBzZXRUaW1lb3V0KCgpID0+IGNvbm5lY3RUb1JlbmRlckNsb3VkKHNlcnZlckJhc2VVcmwpLCAyMDAwKTsKICAgIH0pOwoKICAgIHNvY2tldC5vbignZXJyb3InLCAoKSA9PiB7fSk7CiAgfSk7CgogIHJlcS5vbignZXJyb3InLCAoZXJyKSA9PiB7CiAgICBjb25zb2xlLmVycm9yKGAg4pqg77iPIFdhaXRpbmcgZm9yIENsb3VkIFNlcnZlciAoJHtzZXJ2ZXJCYXNlVXJsfSk6ICR7ZXJyLm1lc3NhZ2V9YCk7CiAgICBzZXRUaW1lb3V0KCgpID0+IGNvbm5lY3RUb1JlbmRlckNsb3VkKHNlcnZlckJhc2VVcmwpLCAzMDAwKTsKICB9KTsKCiAgcmVxLmVuZCgpOwp9Cgpjb25zdCBsb2NhbFByb3h5ID0gaHR0cC5jcmVhdGVTZXJ2ZXIoKGNsaWVudFJlcSwgY2xpZW50UmVzKSA9PiB7CiAgbGV0IHRhcmdldFVybDsKICB0cnkgewogICAgdGFyZ2V0VXJsID0gbmV3IFVSTChjbGllbnRSZXEudXJsKTsKICB9IGNhdGNoIChfKSB7CiAgICBjbGllbnRSZXMud3JpdGVIZWFkKDQwMCk7CiAgICByZXR1cm4gY2xpZW50UmVzLmVuZCgnSW52YWxpZCBVUkwnKTsKICB9CgogIGlmICghY2xvdWRTb2NrZXQgfHwgY2xvdWRTb2NrZXQuZGVzdHJveWVkKSB7CiAgICBjbGllbnRSZXMud3JpdGVIZWFkKDUwMywgeyAnQ29udGVudC1UeXBlJzogJ3RleHQvcGxhaW4nIH0pOwogICAgcmV0dXJuIGNsaWVudFJlcy5lbmQoJ0Ftcml0YSBDbG91ZCBCcmlkZ2UgaXMgY29ubmVjdGluZyB0byBSZW5kZXIuLi4nKTsKICB9CgogIGNvbnN0IHN0cmVhbUlkID0gbmV4dFN0cmVhbUlkKys7CiAgY29uc3QgdGFyZ2V0SG9zdCA9IHRhcmdldFVybC5ob3N0bmFtZTsKICBjb25zdCB0YXJnZXRQb3J0ID0gTnVtYmVyKHRhcmdldFVybC5wb3J0KSB8fCA4MDsKICBjb25zdCBwYXRoV2l0aFF1ZXJ5ID0gKHRhcmdldFVybC5wYXRobmFtZSB8fCAnLycpICsgKHRhcmdldFVybC5zZWFyY2ggfHwgJycpOwoKICBsZXQgcmF3SGVhZGVycyA9IGAke2NsaWVudFJlcS5tZXRob2R9ICR7cGF0aFdpdGhRdWVyeX0gSFRUUC8ke2NsaWVudFJlcS5odHRwVmVyc2lvbn1cclxuYDsKICBmb3IgKGxldCBpID0gMDsgaSA8IGNsaWVudFJlcS5yYXdIZWFkZXJzLmxlbmd0aDsgaSArPSAyKSB7CiAgICBjb25zdCBrID0gY2xpZW50UmVxLnJhd0hlYWRlcnNbaV07CiAgICBjb25zdCB2ID0gY2xpZW50UmVxLnJhd0hlYWRlcnNbaSArIDFdOwogICAgaWYgKGsudG9Mb3dlckNhc2UoKS5zdGFydHNXaXRoKCdwcm94eS0nKSkgY29udGludWU7CiAgICByYXdIZWFkZXJzICs9IGAke2t9OiAke3Z9XHJcbmA7CiAgfQogIHJhd0hlYWRlcnMgKz0gJ0Nvbm5lY3Rpb246IGNsb3NlXHJcblxyXG4nOwoKICBjb25zdCBib2R5Q2h1bmtzID0gW107CiAgY2xpZW50UmVxLm9uKCdkYXRhJywgKGMpID0+IGJvZHlDaHVua3MucHVzaChjKSk7CiAgY2xpZW50UmVxLm9uKCdlbmQnLCAoKSA9PiB7CiAgICBjb25zdCBpbml0aWFsUGF5bG9hZCA9IEJ1ZmZlci5jb25jYXQoW0J1ZmZlci5mcm9tKHJhd0hlYWRlcnMsICd1dGY4JyksIC4uLmJvZHlDaHVua3NdKTsKICAgIHBlbmRpbmdTdHJlYW1zLnNldChzdHJlYW1JZCwgewogICAgICBzb2NrZXQ6IGNsaWVudFJlcy5zb2NrZXQsCiAgICAgIG9uQ29ubmVjdGVkKCkgewogICAgICAgIHNlbmRUdW5uZWxGcmFtZShjbG91ZFNvY2tldCwgeyB0eXBlOiAnREFUQScsIHN0cmVhbUlkIH0sIGluaXRpYWxQYXlsb2FkKTsKICAgICAgfSwKICAgICAgb25FcnJvcihzdGF0dXMsIG1zZykgewogICAgICAgIGNsaWVudFJlcy53cml0ZUhlYWQoc3RhdHVzLCB7ICdDb250ZW50LVR5cGUnOiAndGV4dC9wbGFpbicgfSk7CiAgICAgICAgY2xpZW50UmVzLmVuZChtc2cpOwogICAgICB9CiAgICB9KTsKCiAgICBzZW5kVHVubmVsRnJhbWUoY2xvdWRTb2NrZXQsIHsKICAgICAgdHlwZTogJ0NPTk5FQ1QnLAogICAgICBzdHJlYW1JZCwKICAgICAgaG9zdDogdGFyZ2V0SG9zdCwKICAgICAgcG9ydDogdGFyZ2V0UG9ydCwKICAgICAgZW5jcnlwdGVkOiBmYWxzZQogICAgfSk7CiAgfSk7Cn0pOwoKbG9jYWxQcm94eS5vbignY29ubmVjdCcsIChyZXEsIGNsaWVudFNvY2tldCwgaGVhZCkgPT4gewogIGNvbnN0IFt0YXJnZXRIb3N0LCByYXdQb3J0XSA9IChyZXEudXJsIHx8ICcnKS5zcGxpdCgnOicpOwogIGNvbnN0IHRhcmdldFBvcnQgPSBOdW1iZXIocmF3UG9ydCkgfHwgNDQzOwoKICBpZiAoIWNsb3VkU29ja2V0IHx8IGNsb3VkU29ja2V0LmRlc3Ryb3llZCkgewogICAgY2xpZW50U29ja2V0LndyaXRlKCdIVFRQLzEuMSA1MDMgU2VydmljZSBVbmF2YWlsYWJsZVxyXG5cclxuJyk7CiAgICBjbGllbnRTb2NrZXQuZGVzdHJveSgpOwogICAgcmV0dXJuOwogIH0KCiAgY29uc3Qgc3RyZWFtSWQgPSBuZXh0U3RyZWFtSWQrKzsKICBjbGllbnRTb2NrZXQuc2V0Tm9EZWxheSh0cnVlKTsKCiAgcGVuZGluZ1N0cmVhbXMuc2V0KHN0cmVhbUlkLCB7CiAgICBzb2NrZXQ6IGNsaWVudFNvY2tldCwKICAgIG9uQ29ubmVjdGVkKCkgewogICAgICB0cnkgewogICAgICAgIGNsaWVudFNvY2tldC53cml0ZSgnSFRUUC8xLjEgMjAwIENvbm5lY3Rpb24gRXN0YWJsaXNoZWRcclxuXHJcbicpOwogICAgICAgIGlmIChoZWFkICYmIGhlYWQubGVuZ3RoID4gMCkgewogICAgICAgICAgc2VuZFR1bm5lbEZyYW1lKGNsb3VkU29ja2V0LCB7IHR5cGU6ICdEQVRBJywgc3RyZWFtSWQgfSwgaGVhZCk7CiAgICAgICAgfQogICAgICB9IGNhdGNoIChfKSB7fQogICAgfSwKICAgIG9uRXJyb3Ioc3RhdHVzLCBtc2cpIHsKICAgICAgdHJ5IHsKICAgICAgICBjbGllbnRTb2NrZXQud3JpdGUoYEhUVFAvMS4xICR7c3RhdHVzfSBFcnJvclxyXG5cclxuJHttc2d9XHJcbmApOwogICAgICAgIGNsaWVudFNvY2tldC5kZXN0cm95KCk7CiAgICAgIH0gY2F0Y2ggKF8pIHt9CiAgICB9CiAgfSk7CgogIGNsaWVudFNvY2tldC5vbignZGF0YScsIChjaHVuaykgPT4gewogICAgc2VuZFR1bm5lbEZyYW1lKGNsb3VkU29ja2V0LCB7IHR5cGU6ICdEQVRBJywgc3RyZWFtSWQgfSwgY2h1bmspOwogIH0pOwoKICBjbGllbnRTb2NrZXQub24oJ2VuZCcsICgpID0+IHsKICAgIHNlbmRUdW5uZWxGcmFtZShjbG91ZFNvY2tldCwgeyB0eXBlOiAnRU5EJywgc3RyZWFtSWQgfSk7CiAgICBwZW5kaW5nU3RyZWFtcy5kZWxldGUoc3RyZWFtSWQpOwogIH0pOwoKICBjbGllbnRTb2NrZXQub24oJ2Vycm9yJywgKCkgPT4gewogICAgc2VuZFR1bm5lbEZyYW1lKGNsb3VkU29ja2V0LCB7IHR5cGU6ICdFTkQnLCBzdHJlYW1JZCB9KTsKICAgIHBlbmRpbmdTdHJlYW1zLmRlbGV0ZShzdHJlYW1JZCk7CiAgfSk7CgogIHNlbmRUdW5uZWxGcmFtZShjbG91ZFNvY2tldCwgewogICAgdHlwZTogJ0NPTk5FQ1QnLAogICAgc3RyZWFtSWQsCiAgICBob3N0OiB0YXJnZXRIb3N0LAogICAgcG9ydDogdGFyZ2V0UG9ydCwKICAgIGVuY3J5cHRlZDogdHJ1ZQogIH0pOwp9KTsKCmlmIChyZXF1aXJlLm1haW4gPT09IG1vZHVsZSkgewogIGxvY2FsUHJveHkubGlzdGVuKExPQ0FMX1BST1hZX1BPUlQsICcxMjcuMC4wLjEnLCAoKSA9PiB7CiAgICBjb25zb2xlLmxvZygnPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0nKTsKICAgIGNvbnNvbGUubG9nKCcg8J+OkiBBTVJJVEEgQ0FNUFVTIEJSSURHRSDigJQgREFZIFNDSE9MQVIgQ0xPVUQgQlJJREdFJyk7CiAgICBjb25zb2xlLmxvZygnPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0nKTsKICAgIGNvbnNvbGUubG9nKGAg8J+boe+4jyAgTG9jYWwgQ2hyb21lIEV4dGVuc2lvbiBQcm94eSA6IGh0dHA6Ly8xMjcuMC4wLjE6JHtMT0NBTF9QUk9YWV9QT1JUfWApOwogICAgY29uc29sZS5sb2coYCDimIHvuI8gIFJlbmRlciBDbG91ZCBCcm9rZXIgVVJMICAgICAgOiAke0NMT1VEX1NFUlZFUn1gKTsKICAgIGNvbnNvbGUubG9nKCc9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PScpOwogICAgY29ubmVjdFRvUmVuZGVyQ2xvdWQoQ0xPVURfU0VSVkVSKTsKICB9KTsKfQoKbW9kdWxlLmV4cG9ydHMgPSB7IGxvY2FsUHJveHksIGNvbm5lY3RUb1JlbmRlckNsb3VkIH07Cg==";

        private string workDir;
        private string agentScriptPath;
        private string bridgeScriptPath;
        private string serverUrl = DEFAULT_SERVER_URL;

        // UI Controls - Header
        private Label lblCloudStatus;
        private TabControl tabControl;

        // Hosteller Tab Controls
        private ListView lvRequests;
        private Button btnApproveSelected;
        private Button btnRefreshReqs;
        private TextBox txtHostName;
        private ComboBox cmbHostDuration;
        private Button btnStartInstantHost;
        private Label lblHostServerState;
        private Label lblHostSessionCode;
        private Label lblHostTimer;
        private CheckBox chkAutoCloseApp;
        private Button btnStopHostServer;
        private TextBox txtHostLog;

        // Day Scholar Tab Controls
        private TextBox txtScholarName;
        private ComboBox cmbScholarPurpose;
        private ComboBox cmbScholarDuration;
        private Button btnSendRequest;
        private Label lblScholarReqFeedback;
        private Label lblActiveHostStatus;
        private Label lblScholarBridgeState;
        private Button btnToggleScholarBridge;
        private TextBox txtScholarLog;

        // Runtime State
        private Process hostProcess = null;
        private bool isHostRunning = false;
        private int hostRemainingSeconds = 0;
        private string activeHostSessionCode = "";

        private Process bridgeProcess = null;
        private bool isBridgeRunning = false;

        private System.Windows.Forms.Timer countdownTimer;
        private System.Windows.Forms.Timer pollTimer;
        private List<RequestItem> currentPendingRequests = new List<RequestItem>();

        public MainForm()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            workDir = Path.Combine(Path.GetTempPath(), "AmritaCampusBridgeApp");
            Directory.CreateDirectory(workDir);
            agentScriptPath = Path.Combine(workDir, "agent.js");
            bridgeScriptPath = Path.Combine(workDir, "day-scholar-bridge.js");
            File.WriteAllBytes(agentScriptPath, Convert.FromBase64String(AGENT_B64));
            File.WriteAllBytes(bridgeScriptPath, Convert.FromBase64String(BRIDGE_B64));

            InitializeGui();

            countdownTimer = new System.Windows.Forms.Timer();
            countdownTimer.Interval = 1000;
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Start();

            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 3000;
            pollTimer.Tick += (s, e) => PollServerStatusAsync();
            pollTimer.Start();

            this.Shown += (s, e) => PollServerStatusAsync();
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeGui()
        {
            this.Text = "Amrita Campus Bridge — All-in-One Desktop App (Hosteller & Day Scholar)";
            this.Size = new Size(880, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f);

            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = Color.FromArgb(17, 24, 39)
            };
            this.Controls.Add(topBar);

            Label lblTitle = new Label
            {
                Text = "🎓 Amrita Campus Bridge",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(18, 8),
                Size = new Size(420, 26)
            };
            topBar.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "One-Time Desktop App • Approve Requests, Run Campus Server & Auto-Stop on Timer (No Terminal Needed)",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 35),
                Size = new Size(580, 20)
            };
            topBar.Controls.Add(lblSub);

            lblCloudStatus = new Label
            {
                Text = "🔄 Connecting to Cloud...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(600, 18),
                Size = new Size(250, 24)
            };
            topBar.Controls.Add(lblCloudStatus);

            tabControl = new TabControl
            {
                Location = new Point(14, 72),
                Size = new Size(846, 480),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            this.Controls.Add(tabControl);

            TabPage tabHosteller = new TabPage("🏠 Hosteller Mode (Share College Wi-Fi)")
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(248, 250, 252)
            };
            TabPage tabScholar = new TabPage("🎒 Day Scholar Mode (Access College Websites)")
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(248, 250, 252)
            };

            tabControl.TabPages.Add(tabHosteller);
            tabControl.TabPages.Add(tabScholar);

            BuildHostellerTab(tabHosteller);
            BuildScholarTab(tabScholar);
        }

        private void BuildHostellerTab(TabPage page)
        {
            Panel leftPanel = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(410, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(leftPanel);

            Label lblReqHeader = new Label
            {
                Text = "1. Incoming Day Scholar Requests",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(14, 12),
                Size = new Size(280, 22)
            };
            leftPanel.Controls.Add(lblReqHeader);

            btnRefreshReqs = new Button
            {
                Text = "🔄 Refresh",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(310, 10),
                Size = new Size(86, 26)
            };
            btnRefreshReqs.Click += (s, e) => PollServerStatusAsync();
            leftPanel.Controls.Add(btnRefreshReqs);

            Label lblReqHint = new Label
            {
                Text = "Select a student request below and click Approve & Start Server:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 36),
                Size = new Size(380, 18)
            };
            leftPanel.Controls.Add(lblReqHint);

            lvRequests = new ListView
            {
                Location = new Point(14, 58),
                Size = new Size(382, 165),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                BackColor = Color.FromArgb(11, 15, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            lvRequests.Columns.Add("Student", 115);
            lvRequests.Columns.Add("Time", 55);
            lvRequests.Columns.Add("Code", 78);
            lvRequests.Columns.Add("Purpose", 125);
            leftPanel.Controls.Add(lvRequests);

            btnApproveSelected = new Button
            {
                Text = "✅ Approve & Start Server for Selected Request",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(5, 46, 22),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(14, 230),
                Size = new Size(382, 36)
            };
            btnApproveSelected.Click += BtnApproveSelected_Click;
            leftPanel.Controls.Add(btnApproveSelected);

            Label lblInstantHeader = new Label
            {
                Text = "Or Start an Instant Open Relay Server:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(14, 278),
                Size = new Size(350, 20)
            };
            leftPanel.Controls.Add(lblInstantHeader);

            Label lblHName = new Label
            {
                Text = "Your Name / Hostel:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 304),
                Size = new Size(190, 18)
            };
            leftPanel.Controls.Add(lblHName);

            txtHostName = new TextBox
            {
                Text = "Campus-Hosteller",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 324),
                Size = new Size(210, 26)
            };
            leftPanel.Controls.Add(txtHostName);

            Label lblHDur = new Label
            {
                Text = "Auto-Stop Timer:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(236, 304),
                Size = new Size(150, 18)
            };
            leftPanel.Controls.Add(lblHDur);

            cmbHostDuration = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(236, 324),
                Size = new Size(160, 26)
            };
            cmbHostDuration.Items.AddRange(new object[] { "15 Minutes", "30 Minutes", "60 Minutes" });
            cmbHostDuration.SelectedIndex = 1;
            leftPanel.Controls.Add(cmbHostDuration);

            btnStartInstantHost = new Button
            {
                Text = "▶️ Start Instant Campus Server",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(168, 28, 75),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(14, 362),
                Size = new Size(382, 36)
            };
            btnStartInstantHost.Click += BtnStartInstantHost_Click;
            leftPanel.Controls.Add(btnStartInstantHost);

            Panel rightPanel = new Panel
            {
                Location = new Point(436, 14),
                Size = new Size(388, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(rightPanel);

            lblHostServerState = new Label
            {
                Text = "⚪ SERVER IDLE (Not Sharing)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 14),
                Size = new Size(355, 24)
            };
            rightPanel.Controls.Add(lblHostServerState);

            lblHostSessionCode = new Label
            {
                Text = "Session Code: —",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 40),
                Size = new Size(355, 20)
            };
            rightPanel.Controls.Add(lblHostSessionCode);

            lblHostTimer = new Label
            {
                Text = "00:00",
                Font = new Font("Consolas", 30f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(12, 64),
                Size = new Size(210, 48)
            };
            rightPanel.Controls.Add(lblHostTimer);

            chkAutoCloseApp = new CheckBox
            {
                Text = "Also close this app window when timer hits 00:00",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Checked = false,
                Location = new Point(18, 118),
                Size = new Size(350, 22)
            };
            rightPanel.Controls.Add(chkAutoCloseApp);

            btnStopHostServer = new Button
            {
                Text = "🛑 STOP SERVER NOW",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Location = new Point(16, 146),
                Size = new Size(355, 40)
            };
            btnStopHostServer.Click += (s, e) => StopHostellerServer("Manually stopped by Hosteller");
            rightPanel.Controls.Add(btnStopHostServer);

            Label lblLogTitle = new Label
            {
                Text = "🔒 Live Encrypted Activity Log (*.amrita.edu only):",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 196),
                Size = new Size(355, 18)
            };
            rightPanel.Controls.Add(lblLogTitle);

            txtHostLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(9, 13, 22),
                ForeColor = Color.FromArgb(167, 243, 208),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(16, 218),
                Size = new Size(355, 188)
            };
            rightPanel.Controls.Add(txtHostLog);
            AppendHostLog("Ready. Make sure you are on Amrita College Wi-Fi, then approve a request or click Start.");
        }

        private void BuildScholarTab(TabPage page)
        {
            Panel leftPanel = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(400, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(leftPanel);

            Label lblReqTitle = new Label
            {
                Text = "1. Ask a Hosteller for Campus Wi-Fi",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 14),
                Size = new Size(360, 24)
            };
            leftPanel.Controls.Add(lblReqTitle);

            Label lblSName = new Label
            {
                Text = "Your Name / Roll Number Hint:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 52),
                Size = new Size(350, 20)
            };
            leftPanel.Controls.Add(lblSName);

            txtScholarName = new TextBox
            {
                Text = "Day Scholar Student",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 75),
                Size = new Size(366, 28)
            };
            leftPanel.Controls.Add(txtScholarName);

            Label lblSPurpose = new Label
            {
                Text = "Purpose (Which college portal?):",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 116),
                Size = new Size(350, 20)
            };
            leftPanel.Controls.Add(lblSPurpose);

            cmbScholarPurpose = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 140),
                Size = new Size(366, 28)
            };
            cmbScholarPurpose.Items.AddRange(new object[] {
                "AUMS Login & Attendance Check",
                "Course Registration / Exam Results",
                "Amrita CMS / Library Portal",
                "Lab / Intranet Portal Access"
            });
            cmbScholarPurpose.SelectedIndex = 0;
            leftPanel.Controls.Add(cmbScholarPurpose);

            Label lblSDur = new Label
            {
                Text = "Duration Needed (Hosteller server auto-stops after this):",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 182),
                Size = new Size(366, 20)
            };
            leftPanel.Controls.Add(lblSDur);

            cmbScholarDuration = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 206),
                Size = new Size(366, 28)
            };
            cmbScholarDuration.Items.AddRange(new object[] { "15 Minutes", "30 Minutes", "60 Minutes" });
            cmbScholarDuration.SelectedIndex = 1;
            leftPanel.Controls.Add(cmbScholarDuration);

            btnSendRequest = new Button
            {
                Text = "🚀 Send Request to Hostellers",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(168, 28, 75),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, 250),
                Size = new Size(366, 42)
            };
            btnSendRequest.Click += BtnSendRequest_Click;
            leftPanel.Controls.Add(btnSendRequest);

            lblScholarReqFeedback = new Label
            {
                Text = "Your login ID & password remain 100% encrypted via HTTPS.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(167, 243, 208),
                Location = new Point(16, 306),
                Size = new Size(366, 95)
            };
            leftPanel.Controls.Add(lblScholarReqFeedback);

            Panel rightPanel = new Panel
            {
                Location = new Point(426, 14),
                Size = new Size(398, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(rightPanel);

            Label lblConnTitle = new Label
            {
                Text = "2. Connect Your Browser to Campus Relay",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 14),
                Size = new Size(365, 24)
            };
            rightPanel.Controls.Add(lblConnTitle);

            lblActiveHostStatus = new Label
            {
                Text = "⏳ Checking if a Hosteller is currently online...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(252, 211, 77),
                Location = new Point(16, 48),
                Size = new Size(365, 42)
            };
            rightPanel.Controls.Add(lblActiveHostStatus);

            lblScholarBridgeState = new Label
            {
                Text = "Local Extension Bridge (127.0.0.1:8080): OFF",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 96),
                Size = new Size(365, 22)
            };
            rightPanel.Controls.Add(lblScholarBridgeState);

            btnToggleScholarBridge = new Button
            {
                Text = "🔌 START DAY SCHOLAR VPN BRIDGE",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(5, 46, 22),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, 126),
                Size = new Size(365, 44)
            };
            btnToggleScholarBridge.Click += BtnToggleScholarBridge_Click;
            rightPanel.Controls.Add(btnToggleScholarBridge);

            txtScholarLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(9, 13, 22),
                ForeColor = Color.FromArgb(186, 230, 253),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(16, 186),
                Size = new Size(365, 220)
            };
            rightPanel.Controls.Add(txtScholarLog);
            AppendScholarLog("Click 'START DAY SCHOLAR VPN BRIDGE' and turn ON the Chrome Extension to open *.amrita.edu sites.");
        }

        private void AppendHostLog(string msg)
        {
            if (txtHostLog.InvokeRequired)
            {
                txtHostLog.BeginInvoke(new Action<string>(AppendHostLog), msg);
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtHostLog.AppendText("[" + ts + "] " + msg + Environment.NewLine);
        }

        private void AppendScholarLog(string msg)
        {
            if (txtScholarLog.InvokeRequired)
            {
                txtScholarLog.BeginInvoke(new Action<string>(AppendScholarLog), msg);
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtScholarLog.AppendText("[" + ts + "] " + msg + Environment.NewLine);
        }

        private string EnsureNodeBinary()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("node", "-v")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0) return "node";
                }
            }
            catch { }

            string portableNode = Path.Combine(workDir, "node.exe");
            if (File.Exists(portableNode)) return portableNode;

            AppendHostLog("Downloading portable runtime once (~30MB)...");
            AppendScholarLog("Downloading portable runtime once (~30MB)...");
            Application.DoEvents();
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFile("https://nodejs.org/dist/v20.18.0/win-x64/node.exe", portableNode);
            }
            AppendHostLog("Portable runtime ready!");
            return portableNode;
        }

        private int ParseDurationChoice(ComboBox cmb)
        {
            string txt = cmb.SelectedItem != null ? cmb.SelectedItem.ToString() : "30";
            if (txt.StartsWith("15")) return 15;
            if (txt.StartsWith("60")) return 60;
            return 30;
        }

        private void BtnApproveSelected_Click(object sender, EventArgs e)
        {
            if (lvRequests.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please click on a Day Scholar request in the list first, or use 'Start Instant Campus Server' below.", "Select a Request", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RequestItem req = (RequestItem)lvRequests.SelectedItems[0].Tag;
            StartHostellerServer(req.Id, req.SessionCode, req.DurationMinutes, req.ScholarName);
        }

        private void BtnStartInstantHost_Click(object sender, EventArgs e)
        {
            int mins = ParseDurationChoice(cmbHostDuration);
            StartHostellerServer("", "", mins, "Any Day Scholar");
        }

        private void StartHostellerServer(string requestId, string sessionCode, int durationMins, string scholarName)
        {
            StopHostellerServer(null);

            try
            {
                string nodeBin = EnsureNodeBinary();
                string hostName = string.IsNullOrWhiteSpace(txtHostName.Text) ? "Campus-Hosteller" : txtHostName.Text.Trim();

                StringBuilder args = new StringBuilder();
                args.AppendFormat("\"{0}\" --server \"{1}\" --name \"{2}\" --duration {3}",
                    agentScriptPath, serverUrl, hostName, durationMins);
                if (!string.IsNullOrEmpty(sessionCode))
                    args.AppendFormat(" --session \"{0}\"", sessionCode);
                if (!string.IsNullOrEmpty(requestId))
                    args.AppendFormat(" --request \"{0}\"", requestId);

                ProcessStartInfo psi = new ProcessStartInfo(nodeBin, args.ToString())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                hostProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                hostProcess.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                    {
                        string line = ev.Data.Trim();
                        AppendHostLog(line);
                        Match m = Regex.Match(line, @"Session Code:\s*\[\s*(AMR-[A-Z0-9]+)\s*\]");
                        if (m.Success)
                        {
                            activeHostSessionCode = m.Groups[1].Value;
                            this.BeginInvoke(new Action(() =>
                            {
                                lblHostSessionCode.Text = "Session Code: " + activeHostSessionCode + "  (Serving: " + scholarName + ")";
                            }));
                        }
                    }
                };
                hostProcess.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data)) AppendHostLog("ERR: " + ev.Data.Trim());
                };

                hostProcess.Start();
                hostProcess.BeginOutputReadLine();
                hostProcess.BeginErrorReadLine();

                isHostRunning = true;
                activeHostSessionCode = sessionCode;
                hostRemainingSeconds = durationMins * 60;

                lblHostServerState.Text = "🟢 SERVER LIVE (Sharing *.amrita.edu)";
                lblHostServerState.ForeColor = Color.FromArgb(16, 185, 129);
                lblHostSessionCode.Text = "Session Code: " + (string.IsNullOrEmpty(sessionCode) ? "Assigning..." : sessionCode) + "  (Serving: " + scholarName + ")";
                btnStopHostServer.Enabled = true;

                AppendHostLog("Started Campus Wi-Fi Relay for " + durationMins + " minutes.");
                PollServerStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start Hosteller Relay: " + ex.Message);
            }
        }

        private void StopHostellerServer(string reason)
        {
            isHostRunning = false;
            hostRemainingSeconds = 0;

            if (hostProcess != null)
            {
                try
                {
                    if (!hostProcess.HasExited) hostProcess.Kill();
                }
                catch { }
                hostProcess = null;
            }

            if (!string.IsNullOrEmpty(activeHostSessionCode))
            {
                string codeToRevoke = activeHostSessionCode;
                activeHostSessionCode = "";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/sessions/" + codeToRevoke);
                        req.Method = "DELETE";
                        req.Timeout = 3000;
                        using (WebResponse res = req.GetResponse()) { }
                    }
                    catch { }
                });
            }

            lblHostServerState.Text = "⚪ SERVER IDLE (Not Sharing)";
            lblHostServerState.ForeColor = Color.FromArgb(148, 163, 184);
            lblHostTimer.Text = "00:00";
            btnStopHostServer.Enabled = false;

            if (!string.IsNullOrEmpty(reason))
            {
                AppendHostLog("🛑 " + reason);
                PollServerStatusAsync();
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (!isHostRunning) return;

            if (hostRemainingSeconds > 0)
            {
                hostRemainingSeconds--;
                int m = hostRemainingSeconds / 60;
                int s = hostRemainingSeconds % 60;
                lblHostTimer.Text = string.Format("{0:D2}:{1:D2}", m, s);

                if (hostRemainingSeconds == 0)
                {
                    bool shouldExit = chkAutoCloseApp.Checked;
                    StopHostellerServer("Approved duration finished! Server stopped automatically.");
                    if (shouldExit)
                    {
                        this.Close();
                    }
                }
            }
        }

        private void BtnSendRequest_Click(object sender, EventArgs e)
        {
            string sName = string.IsNullOrWhiteSpace(txtScholarName.Text) ? "Day Scholar" : txtScholarName.Text.Trim();
            string purpose = cmbScholarPurpose.SelectedItem != null ? cmbScholarPurpose.SelectedItem.ToString() : "AUMS Login";
            int duration = ParseDurationChoice(cmbScholarDuration);

            btnSendRequest.Enabled = false;
            lblScholarReqFeedback.Text = "Sending request to Hostellers...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/requests");
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    string json = string.Format("{{\"scholarName\":\"{0}\",\"purpose\":\"{1}\",\"durationMinutes\":{2}}}",
                        sName.Replace("\"", ""), purpose.Replace("\"", ""), duration);
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    using (Stream st = req.GetRequestStream())
                    {
                        st.Write(body, 0, body.Length);
                    }
                    string respText;
                    using (WebResponse res = req.GetResponse())
                    using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                    {
                        respText = sr.ReadToEnd();
                    }

                    Match m = Regex.Match(respText, @"""sessionCode""\s*:\s*""(AMR-[A-Z0-9]+)""");
                    string code = m.Success ? m.Groups[1].Value : "Created";

                    this.BeginInvoke(new Action(() =>
                    {
                        btnSendRequest.Enabled = true;
                        lblScholarReqFeedback.Text = "✅ Request Sent! Session Code: " + code + Environment.NewLine + "Waiting for a Hosteller to click Approve in their app...";
                        AppendScholarLog("Created request for " + duration + " mins (Session Code: " + code + ")");
                        if (!isBridgeRunning) ToggleScholarBridge(true);
                        PollServerStatusAsync();
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        btnSendRequest.Enabled = true;
                        lblScholarReqFeedback.Text = "❌ Error sending request: " + ex.Message;
                    }));
                }
            });
        }

        private void BtnToggleScholarBridge_Click(object sender, EventArgs e)
        {
            ToggleScholarBridge(!isBridgeRunning);
        }

        private void ToggleScholarBridge(bool start)
        {
            if (!start)
            {
                isBridgeRunning = false;
                if (bridgeProcess != null)
                {
                    try { if (!bridgeProcess.HasExited) bridgeProcess.Kill(); } catch { }
                    bridgeProcess = null;
                }
                lblScholarBridgeState.Text = "Local Extension Bridge (127.0.0.1:8080): OFF";
                lblScholarBridgeState.ForeColor = Color.FromArgb(148, 163, 184);
                btnToggleScholarBridge.Text = "🔌 START DAY SCHOLAR VPN BRIDGE";
                btnToggleScholarBridge.BackColor = Color.FromArgb(16, 185, 129);
                btnToggleScholarBridge.ForeColor = Color.FromArgb(5, 46, 22);
                AppendScholarLog("Stopped Day Scholar local proxy bridge.");
                return;
            }

            try
            {
                string nodeBin = EnsureNodeBinary();
                ProcessStartInfo psi = new ProcessStartInfo(nodeBin, "\"" + bridgeScriptPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["AMRITA_BROKER_URL"] = serverUrl;

                bridgeProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                bridgeProcess.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data)) AppendScholarLog(ev.Data.Trim());
                };
                bridgeProcess.Start();
                bridgeProcess.BeginOutputReadLine();

                isBridgeRunning = true;
                lblScholarBridgeState.Text = "🟢 Local Extension Bridge LIVE (127.0.0.1:8080)";
                lblScholarBridgeState.ForeColor = Color.FromArgb(16, 185, 129);
                btnToggleScholarBridge.Text = "🛑 STOP DAY SCHOLAR VPN BRIDGE";
                btnToggleScholarBridge.BackColor = Color.FromArgb(220, 38, 38);
                btnToggleScholarBridge.ForeColor = Color.White;
                AppendScholarLog("Bridge running on 127.0.0.1:8080 -> " + serverUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start Day Scholar Bridge: " + ex.Message);
            }
        }

        private void PollServerStatusAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/status");
                    req.Method = "GET";
                    req.Timeout = 4000;
                    string json;
                    using (WebResponse res = req.GetResponse())
                    using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                    {
                        json = sr.ReadToEnd();
                    }

                    bool hasHost = json.Contains("\"hasActiveHosteller\":true");
                    string activeCode = "";
                    string activeHostName = "";
                    int remSecs = 0;

                    Match mActive = Regex.Match(json, @"""activeSession""\s*:\s*\{([^\}]+)\}");
                    if (mActive.Success)
                    {
                        string block = mActive.Groups[1].Value;
                        Match mc = Regex.Match(block, @"""sessionCode""\s*:\s*""([^""]+)""");
                        if (mc.Success) activeCode = mc.Groups[1].Value;
                        Match mh = Regex.Match(block, @"""hostellerName""\s*:\s*""([^""]+)""");
                        if (mh.Success) activeHostName = mh.Groups[1].Value;
                        Match mr = Regex.Match(block, @"""remainingSeconds""\s*:\s*(\d+)");
                        if (mr.Success) int.TryParse(mr.Groups[1].Value, out remSecs);
                    }

                    List<RequestItem> pending = new List<RequestItem>();
                    MatchCollection items = Regex.Matches(json, @"\{[^{}]*""id""\s*:\s*""REQ-[^""]+""[^{}]*\}");
                    foreach (Match it in items)
                    {
                        string obj = it.Value;
                        string st = ExtractJsonStr(obj, "status");
                        if (st == "pending")
                        {
                            int dur = 30;
                            Match md = Regex.Match(obj, @"""durationMinutes""\s*:\s*(\d+)");
                            if (md.Success) int.TryParse(md.Groups[1].Value, out dur);

                            pending.Add(new RequestItem
                            {
                                Id = ExtractJsonStr(obj, "id"),
                                ScholarName = ExtractJsonStr(obj, "scholarName"),
                                Purpose = ExtractJsonStr(obj, "purpose"),
                                DurationMinutes = dur,
                                Status = st,
                                SessionCode = ExtractJsonStr(obj, "sessionCode")
                            });
                        }
                    }

                    this.BeginInvoke(new Action(() =>
                    {
                        lblCloudStatus.Text = "🟢 Cloud Online (Render)";
                        lblCloudStatus.ForeColor = Color.FromArgb(16, 185, 129);

                        if (hasHost)
                        {
                            int rm = remSecs / 60;
                            int rs = remSecs % 60;
                            lblActiveHostStatus.Text = string.Format("🟢 Hosteller Online: {0} (Code: {1})" + Environment.NewLine + "Time Remaining: {2:D2}:{3:D2} — Ready for *.amrita.edu!", activeHostName, activeCode, rm, rs);
                            lblActiveHostStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        }
                        else
                        {
                            lblActiveHostStatus.Text = "⏳ No Hosteller currently sharing." + Environment.NewLine + "Send a request on the left or ask a Hosteller to click Approve!";
                            lblActiveHostStatus.ForeColor = Color.FromArgb(252, 211, 77);
                        }

                        UpdateRequestsListView(pending);
                    }));
                }
                catch
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            lblCloudStatus.Text = "⚠️ Reconnecting to Cloud...";
                            lblCloudStatus.ForeColor = Color.FromArgb(245, 158, 11);
                        }));
                    }
                }
            });
        }

        private string ExtractJsonStr(string jsonObj, string key)
        {
            Match m = Regex.Match(jsonObj, "\"" + key + @"""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        private void UpdateRequestsListView(List<RequestItem> pending)
        {
            string selectedId = lvRequests.SelectedItems.Count > 0
                ? ((RequestItem)lvRequests.SelectedItems[0].Tag).Id
                : null;

            bool same = pending.Count == currentPendingRequests.Count;
            if (same)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i].Id != currentPendingRequests[i].Id) { same = false; break; }
                }
            }
            if (same) return;

            currentPendingRequests = pending;
            lvRequests.BeginUpdate();
            lvRequests.Items.Clear();
            foreach (var r in pending)
            {
                ListViewItem lvi = new ListViewItem(r.ScholarName);
                lvi.SubItems.Add(r.DurationMinutes + "m");
                lvi.SubItems.Add(r.SessionCode);
                lvi.SubItems.Add(r.Purpose);
                lvi.Tag = r;
                if (r.Id == selectedId) lvi.Selected = true;
                lvRequests.Items.Add(lvi);
            }
            if (lvRequests.Items.Count > 0 && lvRequests.SelectedItems.Count == 0)
            {
                lvRequests.Items[0].Selected = true;
            }
            lvRequests.EndUpdate();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            countdownTimer.Stop();
            pollTimer.Stop();
            StopHostellerServer(null);
            ToggleScholarBridge(false);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
