﻿namespace FSharp.Enterprise

module FileSystem = 
    
    open System
    open System.IO
    open System.Runtime.Caching
    open Serialisation
    open Caching

    let fullPathRelativeTo rootDir file = 
        match Path.IsPathRooted(file) with
        | true -> file
        | false -> 
             match rootDir with
             | Some(root) -> Path.Combine(root, file)
             | None -> Path.GetFullPath(file)
        |> fun x -> new FileInfo(x)
    

    module IO =

        type SearchPattern = string
        type FileFilter = (string -> bool)
        type Path = string

        type IFileIO =
            abstract member Write : Path * 'a -> unit
            abstract member Read : Path -> 'a option
            abstract member ReadAll : Path * FileFilter -> seq<'a>
//            abstract member ReadAll : seq<SearchPattern> -> seq<'a>
            abstract member Delete : Path -> unit
        
        let FileIO (serialiser:ISerialiser<'output>) readOp writeOp =
            { new IFileIO with
                member f.Write(path, payload) = serialiser.Serialise(payload) |> writeOp
                member f.Read(path) = readOp path |> serialiser.Deserialise
                member f.ReadAll(path, filter) = 
                        seq {
                            for file in Directory.EnumerateFiles(path) |> Seq.filter filter do
                                match f.Read(file) with
                                | Some(a) -> yield a 
                                | None -> ()
                        }
                member f.Delete(path) = File.Delete(path)
            }

        let CachedFileIO (serialiser:ISerialiser<'output>) (expiry:TimeSpan) readOp writeOp =
            let onRemoved reason path payload =
                match reason with
                | CacheEntryRemovedReason.Expired -> serialiser.Serialise(payload) |> writeOp
                | _ -> ()
            let cache : ICache<string,_> = Caching.ObjectCache onRemoved MemoryCache.Default
            { new IFileIO with
                member f.Write(path, payload) = cache.Set(path, SlidingExpiry(box payload,expiry))
                member f.Read(path) =
                    match cache.TryGet(path) with
                    | Some(v) -> unbox<_> v
                    | None -> 
                        if File.Exists(path)
                        then Some(readOp path |> serialiser.Deserialise)
                        else None
                member f.ReadAll(path, filter) = 
                    seq {
                        for file in Directory.EnumerateFiles(path) |> Seq.filter filter do
                            match f.Read(file) with
                            | Some(a) -> yield a 
                            | None -> ()
                    }
                member f.Delete(path) =
                   cache.Remove(path) |> ignore
                   if File.Exists(path) then File.Delete(path) 
            }


