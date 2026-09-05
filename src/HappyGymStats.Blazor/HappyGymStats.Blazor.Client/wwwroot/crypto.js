const ITERATIONS = 100000;
const SALT_SIZE = 32;
const NONCE_SIZE = 12;
const TAG_BITS = 128;

function bytesToBase64(bytes) {
  let binary = "";
  const chunk = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunk) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunk));
  }
  return btoa(binary);
}

function base64ToBytes(value) {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

async function deriveWrappingKey(password, salt, iterations) {
  const passwordKey = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveKey"]);

  return crypto.subtle.deriveKey(
    { name: "PBKDF2", salt, iterations, hash: "SHA-256" },
    passwordKey,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt", "decrypt"]);
}

export async function generateAndWrapKey(password) {
  const keyPair = await crypto.subtle.generateKey(
    { name: "ECDH", namedCurve: "P-256" },
    true,
    ["deriveBits"]);

  const publicKeySpki = new Uint8Array(await crypto.subtle.exportKey("spki", keyPair.publicKey));
  const privateKeyPkcs8 = new Uint8Array(await crypto.subtle.exportKey("pkcs8", keyPair.privateKey));
  const salt = crypto.getRandomValues(new Uint8Array(SALT_SIZE));
  const nonce = crypto.getRandomValues(new Uint8Array(NONCE_SIZE));
  const wrappingKey = await deriveWrappingKey(password, salt, ITERATIONS);
  const encryptedPrivateKey = new Uint8Array(await crypto.subtle.encrypt(
    { name: "AES-GCM", iv: nonce, tagLength: TAG_BITS },
    wrappingKey,
    privateKeyPkcs8));

  // Match HappyGymStats.Encryption.KeyWrapping exactly:
  // [4 iterations BE] [32 salt] [12 nonce] [ciphertext] [16 tag].
  // Web Crypto appends the GCM tag to the ciphertext, so its output can be
  // copied directly after the nonce.
  const wrapped = new Uint8Array(4 + SALT_SIZE + NONCE_SIZE + encryptedPrivateKey.length);
  new DataView(wrapped.buffer).setUint32(0, ITERATIONS, false);
  wrapped.set(salt, 4);
  wrapped.set(nonce, 4 + SALT_SIZE);
  wrapped.set(encryptedPrivateKey, 4 + SALT_SIZE + NONCE_SIZE);

  return {
    publicKeySpkiBase64: bytesToBase64(publicKeySpki),
    wrappedPrivateKeyBase64: bytesToBase64(wrapped),
  };
}

export async function unwrapPublicKey(password, wrappedBase64) {
  const wrapped = base64ToBytes(wrappedBase64);
  const minimumLength = 4 + SALT_SIZE + NONCE_SIZE + (TAG_BITS / 8);
  if (wrapped.length < minimumLength) {
    throw new Error("Wrapped key blob too short.");
  }

  const iterations = new DataView(wrapped.buffer, wrapped.byteOffset, wrapped.byteLength)
    .getUint32(0, false);
  if (iterations < 1) {
    throw new Error("Wrapped key iteration count is invalid.");
  }

  const salt = wrapped.slice(4, 4 + SALT_SIZE);
  const nonceOffset = 4 + SALT_SIZE;
  const nonce = wrapped.slice(nonceOffset, nonceOffset + NONCE_SIZE);
  const encryptedPrivateKey = wrapped.slice(nonceOffset + NONCE_SIZE);
  const wrappingKey = await deriveWrappingKey(password, salt, iterations);
  const privateKeyPkcs8 = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: nonce, tagLength: TAG_BITS },
    wrappingKey,
    encryptedPrivateKey);

  const privateKey = await crypto.subtle.importKey(
    "pkcs8",
    privateKeyPkcs8,
    { name: "ECDH", namedCurve: "P-256" },
    true,
    ["deriveBits"]);
  const privateJwk = await crypto.subtle.exportKey("jwk", privateKey);

  const publicKey = await crypto.subtle.importKey(
    "jwk",
    {
      kty: privateJwk.kty,
      crv: privateJwk.crv,
      x: privateJwk.x,
      y: privateJwk.y,
      ext: true,
    },
    { name: "ECDH", namedCurve: "P-256" },
    true,
    []);

  const publicKeySpki = new Uint8Array(await crypto.subtle.exportKey("spki", publicKey));
  return bytesToBase64(publicKeySpki);
}
