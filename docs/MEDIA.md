# Media Platform

Cloudinary is the intended image/video/raw-media provider. Normal media uploads must be direct browser-to-provider uploads authorized by short-lived server-side signed parameters; ASP.NET must not proxy large file bodies. Provider secrets remain server-side. Product metadata stays in PostgreSQL and binaries stay with the provider/CDN.

The existing `ProductAsset` contract already stores provider-neutral URL, MIME type, dimensions, size, role, and sort order. A future `IMediaProvider` implementation should create signed upload intents, verify provider metadata before attachment, soft-detach assets before cleanup, and isolate Cloudinary transformations from commerce-domain code. CI deliberately has no Cloudinary credentials, so external upload verification is not performed.

Catalog managers require `Catalog.Manage`. Allowed media roles are primary, gallery, thumbnail, video, poster, and model 3D. Delivery should use provider responsive transformations and fallback imagery; product cards retain only a primary descriptor while PDP responses carry galleries. Cloudinary failures must not break existing catalog reads.
