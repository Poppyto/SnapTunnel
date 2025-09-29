# SnapTunnel

**SnapTunnel** — *Still Not A Proxy, but a tunnel*

A stealthy network redirection tool that mimics proxy behavior without actually being one. Built for MITM scenarios, snaptunnel leverages local DNS mapping and transparent interception to reroute traffic with precision and minimal footprint.

## 🎩 Philosophy
snaptunnel isn't a proxy. It's a tunnel.
It doesn't ask permission. It reroutes.
It doesn't emulate infrastructure. It bends it.


---

## 🚀 Features

- 🕶️ **Proxyless Spoofing** — no proxy setup, no server overhead  
- 🧠 **Transparent MITM** — intercept and redirect without detection  
- 🗺️ **Local DNS Mapping** — manipulate `/etc/hosts` for instant rerouting  
- 🧩 **Modular Design** — easy to extend, integrate, and automate  
- 🖥️ **Cross-platform** — soon ! works on Linux and macOS

---

## CLI

```
Description:
  SnapTunnel - Still Not A Proxy, but a tunnel

Usage:
  SnapTunnel [options]

Options:
  -v, --verbosity <0-6>                                       Log verbosity (0: Trace, 1: Debug, 
                                                              2: Information, 3: Warning, 4: Error, 
                                                              5: Critical, 6: None)
  -a, --addtohosts                                            Append the domains as 127.0.0.1 into
                                                              %System32%\drivers\etc\hosts and remove
                                                              them when the app exits)
  -i, --installrootcert                                       Install the root certificate in 
                                                              the current user trusted
                                                              root certificate authorities (CAs)
  -u, --uninstallrootcert                                     Uninstall the root certificate from 
                                                              the current user trusted root certificate 
                                                              authorities (CAs)
  -t, --tunnel                                                Create a tunnel
                                                              <[http|https]:src_host:port>[http|https]:dest_host:port[|r
                                                              ewritepath:/(.*)>/api/openai_compat/$1|overwrite:/index.ht
                                                              ml>c:/file/index.html]>
  -?, -h, --help                                              Show help and usage information
  --version                                                   Show version information
```

## ⚙️ Usage

```bash
# install root certificate
snaptunnel -i
snaptunnel --installrootcert

# uninstall root certificate
snaptunnel -u
snaptunnel --uninstallrootcert

# simple redirection with adding domains to etc/host file
snaptunnel --addtohosts --tunnel "https:api.openai.com:443>https:oai.endpoints.kepler.ai.cloud.ovh.net:443"
snaptunnel -a -t "https:api.openai.com:443>https:oai.endpoints.kepler.ai.cloud.ovh.net:443"

# several tunnels
snaptunnel 
    --tunnel "https:api.openai.com:443>https:oai.endpoints.kepler.ai.cloud.ovh.net:443|overwrite=/v1/models/Qwen3-32B>Prompts/model.Qwen3-32B.json|overwrite=/v1/models//Qwen3-32B>Prompts/model.Qwen3-32B.json"
    --tunnel "https:api.anthropic.com:443>http:localhost:8080|overwrite=/v1/organizations>Overwrites/claude.organizations.json"

```

## 🧩 Basic Syntax

Each tunnel is defined using the following structure:

```
protocol:source_host:port>protocol:destination_host:port
```

## 🧩 Advanced features

In addition to basic tunneling, `snaptunnel` supports advanced manipulation of request paths and response content. You can:

- 🔁 **Rewrite request paths** using regular expressions
- 📄 **Overwrite response bodies** with local file content


### 🔀 Rewrite Path


```
# one rule
rewritepath:/(.*)>/api/openai_compat/$1

# several rules
rewritepath:/(.*)>/api/openai_compat/$1|/robots\.txt>/file/robots.txt
```

### 📄 Overwrite bodies


```
# one replace
overwrite:/index.html>c:/file/index.html

# several replaces
overwrite:/index.html>c:/file/index.html|/robots.txt>c:/file/robots.txt

```
