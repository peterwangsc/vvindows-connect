import UIKit

final class InputView: UIView, UIKeyInput {
    var send: ((Input) -> Void)?
    var frameSize = CGSize(width: 16, height: 9) {
        didSet { updateWindowAspect() }
    }
    private var appliedAspect: CGFloat?
    private var scroll = CGPoint.zero
    var hasText: Bool { false }
    override var canBecomeFirstResponder: Bool { true }
    var keyboardType: UIKeyboardType = .asciiCapable
    var autocorrectionType: UITextAutocorrectionType = .no
    var autocapitalizationType: UITextAutocapitalizationType = .none
    var spellCheckingType: UITextSpellCheckingType = .no

    override init(frame: CGRect) {
        super.init(frame: frame)
        addGestureRecognizer(UIHoverGestureRecognizer(target: self, action: #selector(hover)))
        let pan = UIPanGestureRecognizer(target: self, action: #selector(wheel))
        pan.allowedScrollTypesMask = .all
        pan.allowedTouchTypes = []
        addGestureRecognizer(pan)
    }

    required init?(coder: NSCoder) { fatalError() }

    override func didMoveToWindow() {
        super.didMoveToWindow()
        appliedAspect = nil
        updateWindowAspect()
    }

    private func updateWindowAspect() {
        guard frameSize.width > 0, frameSize.height > 0,
              let scene = window?.windowScene else { return }
        let aspect = frameSize.width / frameSize.height
        guard aspect.isFinite, appliedAspect != aspect else { return }
        appliedAspect = aspect
        let width = max(480, scene.effectiveGeometry.coordinateSpace.bounds.width)
        // Lock the native window, not just the video inside a freely resized window.
        scene.requestGeometryUpdate(.Vision(
            size: CGSize(width: width, height: width / aspect),
            minimumSize: CGSize(width: 480, height: 480 / aspect),
            resizingRestrictions: .uniform
        )) { [weak self] error in
            self?.appliedAspect = nil
            NSLog("Could not match desktop window aspect ratio: %@", error.localizedDescription)
        }
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        layer.sublayers?.forEach { $0.frame = bounds }
    }

    private func point(_ location: CGPoint) -> (Int32, Int32)? {
        let scale = min(bounds.width / frameSize.width, bounds.height / frameSize.height)
        let shown = CGSize(width: frameSize.width * scale, height: frameSize.height * scale)
        let origin = CGPoint(x: (bounds.width - shown.width) / 2, y: (bounds.height - shown.height) / 2)
        let x = (location.x - origin.x) / shown.width, y = (location.y - origin.y) / shown.height
        guard (0...1).contains(x), (0...1).contains(y) else { return nil }
        return (Int32(x * 65535), Int32(y * 65535))
    }

    @objc private func hover(_ g: UIHoverGestureRecognizer) {
        guard g.state == .changed || g.state == .began, let (x, y) = point(g.location(in: self)) else { return }
        send?(.move(x, y))
    }

    @objc private func wheel(_ g: UIPanGestureRecognizer) {
        guard g.state == .changed else { scroll = .zero; return }
        let t = g.translation(in: self)
        let dy = Int32((scroll.y - t.y) * 4), dx = Int32((scroll.x - t.x) * 4)
        scroll = t
        if dy != 0 || dx != 0 { send?(.wheel(dy, dx)) }
    }

    private func button(_ event: UIEvent?) -> Int32 {
        event?.buttonMask.contains(.secondary) == true ? 2 : 1
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        becomeFirstResponder()
        guard let t = touches.first, let (x, y) = point(t.location(in: self)) else { return }
        send?(.move(x, y))
        send?(.button(x, y, button(event), down: true))
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let t = touches.first, let (x, y) = point(t.location(in: self)) else { return }
        send?(.move(x, y))
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let t = touches.first, let (x, y) = point(t.location(in: self)) else { return }
        send?(.button(x, y, button(event), down: false))
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) { touchesEnded(touches, with: event) }

    override func pressesBegan(_ presses: Set<UIPress>, with event: UIPressesEvent?) {
        let keys = presses.compactMap(\.key)
        keys.forEach { send?(.key(Int32($0.keyCode.rawValue), down: true)) }
        if keys.isEmpty { super.pressesBegan(presses, with: event) }
    }

    override func pressesEnded(_ presses: Set<UIPress>, with event: UIPressesEvent?) {
        let keys = presses.compactMap(\.key)
        keys.forEach { send?(.key(Int32($0.keyCode.rawValue), down: false)) }
        if keys.isEmpty { super.pressesEnded(presses, with: event) }
    }

    override func pressesCancelled(_ presses: Set<UIPress>, with event: UIPressesEvent?) { pressesEnded(presses, with: event) }

    func insertText(_ text: String) {
        text.unicodeScalars.forEach { send?(.text(Int32($0.value))) }
    }

    func deleteBackward() {
        send?(.key(0x2A, down: true))
        send?(.key(0x2A, down: false))
    }
}

enum Input {
    case move(Int32, Int32)
    case button(Int32, Int32, Int32, down: Bool)
    case wheel(Int32, Int32)
    case key(Int32, down: Bool)
    case text(Int32)

    var record: Data {
        let (kind, flags, a, b, c): (UInt8, UInt8, Int32, Int32, Int32) = switch self {
        case .move(let x, let y): (1, 0, x, y, 0)
        case .button(let x, let y, let button, let down): (2, down ? 1 : 0, x, y, button)
        case .wheel(let v, let h): (3, 0, v, h, 0)
        case .key(let usage, let down): (4, down ? 1 : 0, usage, 0, 0)
        case .text(let scalar): (5, 0, scalar, 0, 0)
        }
        var data = Data([kind, flags, 0, 0])
        for value in [a, b, c] { withUnsafeBytes(of: value.littleEndian) { data.append(contentsOf: $0) } }
        return data
    }
}
