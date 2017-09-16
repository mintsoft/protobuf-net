# protobuf-net.Pool

This is a fork of protobuf-net by mgravell with a different implementation of the bufferpool that underlies the serializer. We've experienced issues with LOH fragmentation caused by the BufferPool.cs constantly resizing and reallocating byte[]. Once they hit 85k the arrays are allocated on the LOH, this allocation and resizing behaviour can cause quite substantial LOH fragmentation overtime and with certain object patterns. This implementation when it creates a larger buffer, saves it in the bufferpool for reuse. This has the effect that over-time all the buffers end up larger, so the base footprint of the library is also larger. 

protobuf-net is a contract based serializer for .NET code, that happens to write data in the "protocol buffers" serialization format engineered by Google. The API, however, is very different to Google's, and follows typical .NET patterns (it is broadly comparable, in usage, to XmlSerializer, DataContractSerializer, etc). It should work for most .NET languages that write standard types and can use attributes.

## Release Notes & Usage etc

You are best looking at the original : [Change history and pending changes are here](http://mgravell.github.io/protobuf-net/releasenotes)

This is packaged with a version higher than the original (i.e. original is 2.3.3.0, this is 2.3.3.1) so it can be dropped in and used with a BindingRedirect in the places you want it.
