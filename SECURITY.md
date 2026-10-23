# Security policy

## Intended use and trust boundary

KeyboardShare captures global physical keyboard events in Host mode and can inject keyboard input into applications in Client mode. Use it only between computers and accounts you trust on a trusted LAN. Do not forward the listening TCP port through a router or expose it to the public internet. Restrict the Host firewall rule to the Client's reserved IP address, or to a trusted local subnet for DHCP, and the Private profile. A local-subnet rule allows more computers to attempt connections; peer filtering and token authentication still apply. Running elevated increases the impact of a compromised peer or token.

Anyone controlling an authenticated Host or possessing the shared token and an allowed network identity may be able to control applications on the Client. IP filtering alone is not authentication. This is a small user-mode utility, not a security boundary against administrators, malware, or compromised endpoints.

## Implemented transport and authentication

- The Host accepts one Client at a time. `PeerIP` may contain a literal IP or computer/DNS name. For every accepted connection the Host resolves the configured name and compares the incoming IP with the returned addresses; failed or empty lookups reject the connection. The Client resolves its configured Host name on every retry and connects only to those returned addresses. Literal IP settings remain supported. The app does not cache resolution results; Windows/DNS caches can delay changes. Names must be resolvable on both machines. DNS is not an authentication mechanism: token proofs are still required if a name is spoofed or an address is reassigned.
- `SslStream` negotiates TLS 1.2 or TLS 1.3 as supported by Windows. A self-signed RSA-2048 server certificate is generated at Host startup.
- The TLS certificate callback accepts the presented certificate; **trust does not come from CA validation or hostname validation**. Both sides must then complete the application's shared-token authentication before processing input frames.
- Each side generates a 32-byte random nonce. The authentication transcript is the server nonce, client nonce, and SHA-256 hash of the server certificate. Proofs are HMAC-SHA256, keyed by SHA-256 of the UTF-8 token, with distinct server/client role bytes. Received proofs use a fixed-time comparison. The certificate hash binds the proof to the server certificate used on the TLS connection.
- Client name-resolution/connection attempts and the combined TLS/application handshake have separate five-second deadlines. A candidate address has a two-second connection budget within that total; Host-side peer-name resolution has a five-second deadline. The application has a bounded 1,024-frame outbound queue and closes the connection on queue overflow. It has no general-purpose rate limiting or independent security audit.

The randomly generated default token contains 32 random bytes encoded as Base64. Validation requires at least 32 characters but **does not measure entropy**. A long predictable password remains weak and the authentication exchange is not a password-authenticated key exchange. Use the generated random token and transfer it through a trusted channel.

## Local data and limitations

`%LOCALAPPDATA%\KeyboardShare\config.json` contains the token in **plaintext**, protected by the permissions of your Windows profile. Tokens are not encrypted using DPAPI. Do not commit, upload, or screenshot this file or an unmasked token. Set the same new random token on both computers if one is exposed. `.gitignore` excludes common local secret/configuration files, but cannot detect every possible filename; review changes before committing.

The application does not intentionally log keystrokes or tokens. Exception logs can contain addresses, local paths, or environment details, so review and redact logs before sharing. The Client attempts to release its tracked held keys when a session ends. Abrupt process termination or an inaccessible desktop can prevent cleanup. Network timeouts and automatic reconnect are recovery mechanisms, not availability guarantees.

The protocol and implementation have not undergone independent penetration testing or a formal cryptographic review. No security guarantee or response-time SLA is provided. Security fixes should target the latest source/release; older releases have no separate maintenance commitment.

## Report a vulnerability privately

Use this repository's **Security / Security and quality → Report a vulnerability** option when available. Include the affected version or commit, sanitized reproduction steps, impact, and relevant environment details. Do not attach real tokens or private keyboard content. Do not publish exploit details in a public issue while arranging disclosure.

If the private reporting option is unavailable, open an issue containing only a request for a private security contact, with no vulnerability details or sensitive data. A private contact address is not configured in this source template.

**Maintainer setup before public release:** enable GitHub private vulnerability reporting in the repository's security settings, or replace the fallback above with a working private contact method. Adding this file alone does not enable private reporting. See [GitHub's private reporting documentation](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/report-privately).
