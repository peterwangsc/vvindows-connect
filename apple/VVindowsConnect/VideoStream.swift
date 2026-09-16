import AVFoundation
import CoreMedia
import Foundation
import SwiftUI

@MainActor
final class VideoStream {
    let layer = AVSampleBufferDisplayLayer()
    private var sps: Data?
    private var pps: Data?
    private var format: CMVideoFormatDescription?

    func reset() {
        layer.sampleBufferRenderer.flush()
        sps = nil
        pps = nil
        format = nil
    }

    func enqueue(annexB: Data, keyframe: Bool) {
        var avcc = Data()
        for nal in nalUnits(annexB) {
            switch nal.first.map({ $0 & 0x1f }) {
            case 7: sps = nal
            case 8: pps = nal
            default:
                var length = UInt32(nal.count).bigEndian
                avcc.append(Data(bytes: &length, count: 4))
                avcc.append(nal)
            }
        }
        if keyframe, let sps, let pps {
            let sets = [sps, pps]
            sets[0].withUnsafeBytes { s in sets[1].withUnsafeBytes { p in
                let pointers = [s.baseAddress!.assumingMemoryBound(to: UInt8.self), p.baseAddress!.assumingMemoryBound(to: UInt8.self)]
                var description: CMVideoFormatDescription?
                CMVideoFormatDescriptionCreateFromH264ParameterSets(allocator: nil, parameterSetCount: 2, parameterSetPointers: pointers, parameterSetSizes: [sps.count, pps.count], nalUnitHeaderLength: 4, formatDescriptionOut: &description)
                if let description, format == nil || !CMFormatDescriptionEqual(description, otherFormatDescription: format) { format = description }
            } }
        }
        guard let format, !avcc.isEmpty else { return }
        var block: CMBlockBuffer?
        CMBlockBufferCreateWithMemoryBlock(allocator: nil, memoryBlock: nil, blockLength: avcc.count, blockAllocator: nil, customBlockSource: nil, offsetToData: 0, dataLength: avcc.count, flags: 0, blockBufferOut: &block)
        guard let block else { return }
        avcc.withUnsafeBytes { CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block, offsetIntoDestination: 0, dataLength: avcc.count) }
        var sample: CMSampleBuffer?
        var timing = CMSampleTimingInfo(duration: .invalid, presentationTimeStamp: .invalid, decodeTimeStamp: .invalid)
        var size = avcc.count
        CMSampleBufferCreateReady(allocator: nil, dataBuffer: block, formatDescription: format, sampleCount: 1, sampleTimingEntryCount: 1, sampleTimingArray: &timing, sampleSizeEntryCount: 1, sampleSizeArray: &size, sampleBufferOut: &sample)
        guard let sample, let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true) as? [NSMutableDictionary] else { return }
        attachments.first?[kCMSampleAttachmentKey_DisplayImmediately] = true
        if layer.sampleBufferRenderer.status == .failed { layer.sampleBufferRenderer.flush() }
        layer.sampleBufferRenderer.enqueue(sample)
    }

    private func nalUnits(_ data: Data) -> [Data] {
        var units: [Data] = []
        var start: Int?
        var i = data.startIndex
        while i + 2 < data.endIndex {
            if data[i] == 0, data[i + 1] == 0, data[i + 2] == 1 {
                if let start { units.append(data[start..<(data[i - 1] == 0 && i - 1 > start ? i - 1 : i)]) }
                start = i + 3
                i += 3
            } else { i += 1 }
        }
        if let start, start < data.endIndex { units.append(data[start...]) }
        return units
    }
}

struct VideoView: UIViewRepresentable {
    let stream: VideoStream
    let frameSize: CGSize
    let send: (Input) -> Void

    func makeUIView(context: Context) -> InputView {
        stream.layer.videoGravity = .resizeAspect
        let view = InputView()
        view.layer.addSublayer(stream.layer)
        return view
    }

    func updateUIView(_ view: InputView, context: Context) {
        view.frameSize = frameSize
        view.send = send
    }
}
