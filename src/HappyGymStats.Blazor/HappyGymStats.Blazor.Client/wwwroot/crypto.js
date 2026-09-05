const iterations = 100000;
const saltLength = 32;
const nonceLength = 12;
const tagLength = 16;
const headerLength = 4 + saltLength + nonceLength;
const minimumWrappedLength = headerLength + tagLength;

function toBase64(bytes) {
    let binary = "";
    for (const byte of bytes)
        binary += String.fromCharCode(byte);
    return btoa(binary);
}

function fromBase64(value) {
    const binary = atob(value);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++)
        bytes[index] = binary.charCodeAt(index);
    return bytes;
}

async function deriveWrappingKey(password, salt, iterationCount) {
    const passwordKey = await crypto.subtle.importKey(
        "raw",
        new TextEncoder().encode(password),
        "PBKDF2",
        false,
        ["deriveKey"]);

    return crypto.subtle.deriveKey(
        { name: "PBKDF2", hash: "SHA-256", salt, iterations: iterationCount },
        passwordKey,
        { name: "AES-GCM", length: 256 },
        false,
        ["encrypt", "decrypt"]);
}

export async function generateWrappedKeyPair(password) {
    const pair = await crypto.subtle.generateKey(
        { name: "ECDH", namedCurve: "P-256" },
        true,
        ["deriveBits"]);

    const publicKeySpki = new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey));
    const privateKeyPkcs8 = new Uint8Array(await crypto.subtle.exportKey("pkcs8", pair.privateKey));
    const salt = crypto.getRandomValues(new Uint8Array(saltLength));
    const nonce = crypto.getRandomValues(new Uint8Array(nonceLength));
    const wrappingKey = await deriveWrappingKey(password, salt, iterations);
    const ciphertextAndTag = new Uint8Array(await crypto.subtle.encrypt(
        { name: "AES-GCM", iv: nonce, tagLength: tagLength * 8 },
        wrappingKey,
        privateKeyPkcs8));

    // Keep the existing KeyWrapping wire layout for compatibility with stored keys:
    // [4-byte PBKDF2 iterations, big endian][32-byte salt][12-byte nonce][ciphertext][16-byte tag].
    const wrapped = new Uint8Array(headerLength + ciphertextAndTag.length);
    new DataView(wrapped.buffer).setUint32(0, iterations, false);
    wrapped.set(salt, 4);
    wrapped.set(nonce, 4 + saltLength);
    wrapped.set(ciphertextAndTag, headerLength);

    return {
        publicKeySpki: toBase64(publicKeySpki),
        wrappedPrivateKey: toBase64(wrapped)
    };
}

export async function unwrapPublicKeySpki(wrappedBase64, password) {
    try {
        const wrapped = fromBase64(wrappedBase64);
        if (wrapped.length < minimumWrappedLength)
            return null;

        const storedIterations = new DataView(
            wrapped.buffer,
            wrapped.byteOffset,
            wrapped.byteLength).getUint32(0, false);

        // Stored data is attacker/user controlled. Refuse tampered work factors instead of
        // letting localStorage drive an unbounded PBKDF2 workload in the browser.
        if (storedIterations !== iterations)
            return null;

        const salt = wrapped.slice(4, 4 + saltLength);
        const nonce = wrapped.slice(4 + saltLength, headerLength);
        const ciphertextAndTag = wrapped.slice(headerLength);
        const wrappingKey = await deriveWrappingKey(password, salt, storedIterations);
        const privateKeyPkcs8 = await crypto.subtle.decrypt(
            { name: "AES-GCM", iv: nonce, tagLength: tagLength * 8 },
            wrappingKey,
            ciphertextAndTag);

        const privateKey = await crypto.subtle.importKey(
            "pkcs8",
            privateKeyPkcs8,
            { name: "ECDH", namedCurve: "P-256" },
            true,
            ["deriveBits"]);
        const privateJwk = await crypto.subtle.exportKey("jwk", privateKey);
        delete privateJwk.d;
        privateJwk.key_ops = [];
        const publicKey = await crypto.subtle.importKey(
            "jwk",
            privateJwk,
            { name: "ECDH", namedCurve: "P-256" },
            true,
            []);
        const publicKeySpki = new Uint8Array(await crypto.subtle.exportKey("spki", publicKey));
        return toBase64(publicKeySpki);
    }
    catch {
        // AES-GCM authentication failures and malformed key material are intentionally
        // indistinguishable to the UI: both mean the supplied password/key cannot unlock.
        return null;
    }
}
