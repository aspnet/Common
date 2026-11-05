// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import type { CSSProperties } from 'react';
import { useId } from 'react';
import { Card, makeStyles, mergeClasses } from '@fluentui/react-components';
import ReactMarkdown, { type Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useReportContext } from '../core/ReportContext';
import { type ChatMessageDisplay, isTextContent, isImageContent } from '../core/Summary';
import { useReportStyles } from '../styles/reportStyles';
import './transcript.css';

type FunctionCallLike = AIContent & {
    $type: 'functionCall';
    callId?: string;
    name?: string;
    arguments?: unknown;
};

type FunctionResultLike = AIContent & {
    $type: 'functionResult';
    callId?: string;
    result?: unknown;
};

const isFunctionCall = (content: AIContent): content is FunctionCallLike =>
    content?.$type === 'functionCall';

const isFunctionResult = (content: AIContent): content is FunctionResultLike =>
    content?.$type === 'functionResult';

const T_ACCENT = 'var(--tool-accent-text)';
const T_TINT = 'color-mix(in srgb, var(--palette-teal-foreground) 7%, var(--neutral-background-1))';
const T_HEAD = 'color-mix(in srgb, var(--palette-teal-foreground) 14%, var(--neutral-background-1))';
const T_BORDER = 'color-mix(in srgb, var(--palette-teal-foreground) 30%, var(--neutral-background-1))';

const isUserSide = (role: string) => role.toLowerCase() === 'user';
const isSystemSide = (role: string) => role.toLowerCase() === 'system';
const isToolSide = (role: string) => role.toLowerCase() === 'tool';

const safeJson = (value: unknown, pretty: boolean): string => {
    try {
        return JSON.stringify(value, null, pretty ? 2 : 0) ?? String(value);
    } catch {
        return String(value);
    }
};

const reportDate = (createdAt: string | undefined): string | undefined => {
    const dm = (createdAt ?? '').match(/(\d{4})-(\d{2})-(\d{2})/);
    if (!dm) return undefined;
    const months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    return `${months[(+dm[2]) - 1]} ${+dm[3]}, ${dm[1]}`;
};

type ToolSectionVM = {
    name: string;
    callText: string;
    hasResult: boolean;
    resultText: string;
    callId?: string;
};

type BubbleVM = {
    tools: ToolSectionVM[];
    text: AIContent | null;
    imageAlt?: string;
};

type GroupVM =
    | { kind: 'group'; role: 'user' | 'assistant'; bubbles: BubbleVM[] }
    | { kind: 'system'; content: AIContent; imageAlt?: string };

const roleLabel = (role: string): string => {
    if (isUserSide(role)) return 'the user';
    if (isSystemSide(role)) return 'the system';
    if (isToolSide(role)) return 'a tool';
    return 'the assistant';
};

const deriveImageAlt = (message: ChatMessageDisplay): string =>
    `Image shared by ${roleLabel(message.role)}`;

const buildGroups = (messages: ChatMessageDisplay[], prettifyJson: boolean): GroupVM[] => {
    const groups: GroupVM[] = [];
    let pending: ToolSectionVM[] = [];
    let openAssistant: Extract<GroupVM, { kind: 'group' }> | null = null;

    const flushPending = () => {
        if (openAssistant && pending.length) {
            openAssistant.bubbles.push({ tools: pending, text: null });
        }
        pending = [];
    };

    for (const m of messages) {
        const content = m.content;

        if (isFunctionResult(content)) {
            const resultText = safeJsonMaybeString(content.result, prettifyJson);
            const resultCallId = content.callId;
            const byId = resultCallId !== undefined
                ? pending.find((t) => !t.hasResult && t.callId === resultCallId)
                : undefined;
            const target = byId ?? [...pending].reverse().find((t) => !t.hasResult);
            if (target) {
                target.hasResult = true;
                target.resultText = resultText;
            } else {
                pending.push({ name: content.callId ?? 'result', callText: '', hasResult: true, resultText, callId: resultCallId });
            }
            continue;
        }

        if (isFunctionCall(content)) {
            if (!openAssistant) {
                openAssistant = { kind: 'group', role: 'assistant', bubbles: [] };
                groups.push(openAssistant);
            }
            pending.push({
                name: content.name ?? 'function',
                callText: safeJson(content.arguments ?? {}, prettifyJson),
                hasResult: false,
                resultText: '',
                callId: content.callId,
            });
            continue;
        }

        if (isSystemSide(m.role)) {
            flushPending();
            openAssistant = null;
            const imageAlt = isImageContent(content) ? deriveImageAlt(m) : undefined;
            groups.push({ kind: 'system', content, imageAlt });
            continue;
        }

        if (isUserSide(m.role)) {
            flushPending();
            openAssistant = null;
            const imageAlt = isImageContent(content) ? deriveImageAlt(m) : undefined;
            const prev = groups[groups.length - 1];
            if (prev && prev.kind === 'group' && prev.role === 'user') {
                prev.bubbles.push({ tools: [], text: content, imageAlt });
            } else {
                groups.push({ kind: 'group', role: 'user', bubbles: [{ tools: [], text: content, imageAlt }] });
            }
            continue;
        }

        if (isToolSide(m.role)) {
            if (!openAssistant) {
                openAssistant = { kind: 'group', role: 'assistant', bubbles: [] };
                groups.push(openAssistant);
            }
            pending.push({ name: 'tool', callText: safeJson(content, prettifyJson), hasResult: false, resultText: '' });
            continue;
        }

        if (!openAssistant) {
            openAssistant = { kind: 'group', role: 'assistant', bubbles: [] };
            groups.push(openAssistant);
        }
        openAssistant.bubbles.push({
            tools: pending,
            text: content,
            imageAlt: isImageContent(content) ? deriveImageAlt(m) : undefined,
        });
        pending = [];
    }
    flushPending();

    return groups;
};

const safeJsonMaybeString = (value: unknown, pretty: boolean): string =>
    typeof value === 'string' ? value : safeJson(value ?? null, pretty);

const useStyles = makeStyles({
    iconMd: { width: '18px', height: '18px' },
    iconSm: { width: '16px', height: '16px' },

    mdText: {
        whiteSpace: 'normal',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--spacing-xxs)',
        wordBreak: 'break-word',
    },
    codeBlock: {
        margin: 0,
        fontFamily: 'var(--font-family-monospace)',
        fontSize: 'var(--font-size-200)',
        lineHeight: 'var(--line-height-200)',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
        color: 'var(--neutral-foreground-1)',
    },
    plainText: { whiteSpace: 'pre-wrap' },
    inlineImage: {
        maxWidth: '100%',
        maxHeight: '320px',
        borderRadius: 'var(--radius-small)',
    },

    toolSection: {
        width: '100%',
        boxSizing: 'border-box',
        border: `1px solid ${T_BORDER}`,
        background: T_TINT,
        borderRadius: 'var(--radius-large)',
        overflow: 'hidden',
    },
    toolHead: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-s)',
        padding: 'var(--spacing-s) var(--spacing-m)',
        background: T_HEAD,
        fontFamily: 'var(--font-family-base)',
        fontSize: 'var(--font-size-200)',
        fontWeight: 'var(--font-weight-semibold)',
        color: T_ACCENT,
    },
    toolTitle: {
        flex: '1 1 auto',
        minWidth: 0,
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
    },
    toolIcon: {
        flex: 'none',
        display: 'inline-flex',
        alignItems: 'center',
        color: T_ACCENT,
    },
    toolCap: {
        padding: 'var(--spacing-s) var(--spacing-m) 0',
        background: T_TINT,
        borderTop: `1px solid ${T_BORDER}`,
        fontFamily: 'var(--font-family-base)',
        fontSize: 'var(--font-size-100)',
        fontWeight: 'var(--font-weight-semibold)',
        textTransform: 'uppercase',
        letterSpacing: '0.4px',
        color: T_ACCENT,
    },
    toolBody: {
        margin: 0,
        padding: 'var(--spacing-xxs) var(--spacing-m) var(--spacing-s)',
        background: T_TINT,
        fontFamily: 'var(--font-family-monospace)',
        fontSize: 'var(--font-size-200)',
        lineHeight: 'var(--line-height-200)',
        color: 'var(--neutral-foreground-2)',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
    },

    bubbleMsg: { marginTop: 'var(--spacing-xxs)' },
    bubbleBody: {
        position: 'relative',
        borderRadius: 'var(--radius-large)',
        color: 'var(--neutral-foreground-1)',
        wordBreak: 'break-word',
    },
    bubbleTextWrap: {
        padding: 'var(--spacing-xxs) var(--spacing-s)',
        fontSize: 'var(--font-size-300)',
        lineHeight: 'var(--line-height-400)',
    },
    bubbleTail: {
        position: 'absolute',
        top: 0,
        width: '8px',
        height: '12px',
    },

    avatarBase: {
        width: '32px',
        height: '32px',
        alignSelf: 'start',
        borderRadius: 'var(--radius-circular)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'var(--white)',
        flex: 'none',
        gridRow: 1,
    },
    groupGrid: {
        display: 'grid',
        columnGap: 'var(--spacing-s)',
        marginTop: 'var(--spacing-l)',
    },
    groupCol: {
        gridRow: 1,
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
    },
    groupHead: {
        display: 'flex',
        gap: 'var(--spacing-s)',
        alignItems: 'baseline',
        margin: '0 var(--spacing-xs) var(--spacing-xxs)',
    },
    groupName: {
        fontSize: 'var(--font-size-200)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-1)',
    },
    groupTime: {
        fontSize: 'var(--font-size-100)',
        color: 'var(--neutral-foreground-3)',
        whiteSpace: 'nowrap',
    },

    systemWrap: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        marginTop: 'var(--spacing-l)',
    },
    systemBubble: {
        borderRadius: 'var(--radius-xlarge)',
        padding: 'var(--spacing-s) var(--spacing-xxl)',
        fontSize: 'var(--font-size-200)',
        lineHeight: 1.5,
        maxWidth: '88%',
        textAlign: 'center',
        color: 'var(--neutral-foreground-3)',
        background: 'var(--neutral-background-2)',
        border: '1px solid var(--neutral-stroke-1)',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
    },

    card: { margin: '-12px' },
    headerRow: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 'var(--spacing-m-nudge)',
        padding: 'var(--spacing-m-nudge) var(--spacing-l)',
        borderBottom: '1px solid var(--neutral-stroke-3)',
    },
    blockBody: {
        padding: 'var(--spacing-xs) var(--spacing-l) var(--spacing-l)',
        display: 'flex',
        flexDirection: 'column',
    },
    divider: {
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-m)',
        margin: 'var(--spacing-xs) 0 var(--spacing-s)',
        color: 'var(--neutral-foreground-3)',
    },
    dividerLine: { flex: 1, height: '1px', background: 'var(--neutral-stroke-2)' },
    dividerLabel: {
        fontSize: 'var(--font-size-100)',
        fontWeight: 'var(--font-weight-semibold)',
        letterSpacing: '0.4px',
        textTransform: 'uppercase',
    },
    empty: {
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-3)',
        fontStyle: 'italic',
    },
});

const SparkleIcon = () => {
    const classes = useStyles();
    return (
        <svg viewBox="0 0 20 20" fill="currentColor" className={classes.iconMd} aria-hidden="true">
            <path d="M10 1.6l1.2 2.6 2.6 1.2-2.6 1.2L10 9.2 8.8 6.6 6.2 5.4l2.6-1.2zM5 11l.8 1.7L7.5 13.5 5.8 14.3 5 16l-.8-1.7L2.5 13.5l1.7-.8zM15 11l.8 1.7 1.7.8-1.7.8L15 16l-.8-1.7-1.7-.8 1.7-.8z" />
        </svg>
    );
};

const PersonIcon = () => {
    const classes = useStyles();
    return (
        <svg viewBox="0 0 20 20" fill="currentColor" className={classes.iconMd} aria-hidden="true">
            <circle cx="10" cy="6" r="3.2" />
            <path d="M3.5 17c0-3.3 2.9-5.5 6.5-5.5s6.5 2.2 6.5 5.5z" />
        </svg>
    );
};

const WrenchIcon = () => {
    const classes = useStyles();
    return (
        <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" className={classes.iconSm} aria-hidden="true">
            <path d="M13.7 3.5a3.5 3.5 0 00-4.6 4.2l-5 5a1.4 1.4 0 002 2l5-5a3.5 3.5 0 004.2-4.6l-2 2-1.6-.4-.4-1.6z" />
        </svg>
    );
};

const MD_COMPONENTS: Components = {
    a: ({ children, href }) => (
        <a href={href} target="_blank" rel="noopener noreferrer">{children}</a>
    ),
    // The block scrolls horizontally, so it needs to be focusable to be reachable by keyboard.
    pre: ({ children }) => (
        <pre tabIndex={0} role="region" aria-label="Code block">{children}</pre>
    ),
    // Headings in evaluation data would otherwise land in the report's own document outline.
    h1: ({ children }) => <div className="md-h1">{children}</div>,
    h2: ({ children }) => <div className="md-h2">{children}</div>,
    h3: ({ children }) => <div className="md-h3">{children}</div>,
    h4: ({ children }) => <div className="md-h4">{children}</div>,
    h5: ({ children }) => <div className="md-h4">{children}</div>,
    h6: ({ children }) => <div className="md-h4">{children}</div>,
};

export const TextNode = ({ content, altHint }: { content: AIContent; altHint?: string }) => {
    const { renderMarkdown, prettifyJson } = useReportContext();
    const classes = useStyles();

    if (isTextContent(content)) {
        const trimmed = content.text.trim();
        if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
            try {
                const parsed = JSON.parse(trimmed);
                return <pre className={classes.codeBlock}>{JSON.stringify(parsed, null, prettifyJson ? 2 : 0)}</pre>;
            } catch {
                // not valid JSON after all; fall through and render as markdown/plain text.
            }
        }
        return renderMarkdown ? (
            <div className={mergeClasses('eval-md', classes.mdText)}>
                <ReactMarkdown remarkPlugins={[remarkGfm]} components={MD_COMPONENTS}>{content.text}</ReactMarkdown>
            </div>
        ) : (
            <span className={classes.plainText}>{content.text}</span>
        );
    }

    if (isImageContent(content)) {
        const imageUrl = (content as UriContent | DataContent).uri;
        return <img src={imageUrl} alt={altHint ?? 'Image from the conversation'} className={classes.inlineImage} />;
    }

    return <pre className={classes.codeBlock}>{safeJson(content, prettifyJson)}</pre>;
};

const ToolSection = ({ tool }: { tool: ToolSectionVM }) => {
    const classes = useStyles();
    return (
        <div className={classes.toolSection}>
            <div className={classes.toolHead}>
                <span className={classes.toolTitle}>{`Tool call: ${tool.name}`}</span>
                <span className={classes.toolIcon}><WrenchIcon /></span>
            </div>
            <div className={classes.toolCap}>Input</div>
            <pre className={classes.toolBody}>{tool.callText}</pre>
            {tool.hasResult && (
                <>
                    <div className={classes.toolCap}>Output</div>
                    <pre className={classes.toolBody}>{tool.resultText}</pre>
                </>
            )}
        </div>
    );
};

const Bubble = ({ bubble, me, first }: { bubble: BubbleVM; me: boolean; first: boolean }) => {
    const classes = useStyles();
    const fill = me ? 'var(--teams-mine)' : 'var(--teams-other)';
    const hasTools = bubble.tools.length > 0;
    const hasText = bubble.text !== null;

    const bubbleDynamicStyle: CSSProperties = {
        background: fill,
        ...(hasTools
            ? { padding: 'var(--spacing-xs)', display: 'flex', flexDirection: 'column', gap: 'var(--spacing-xs)' }
            : { padding: 'var(--spacing-s-nudge) var(--spacing-m)', fontSize: 'var(--font-size-300)', lineHeight: 'var(--line-height-400)' }),
        ...(first ? (me ? { borderTopRightRadius: 'var(--radius-small)' } : { borderTopLeftRadius: 'var(--radius-small)' }) : {}),
    };
    const tailDynamicStyle: CSSProperties = {
        ...(me ? { right: '-6px' } : { left: '-6px' }),
        background: fill,
        clipPath: me ? 'polygon(0 0, 0 100%, 100% 0)' : 'polygon(100% 0, 100% 100%, 0 0)',
    };

    return (
        <div className={classes.bubbleMsg} style={{ maxWidth: hasTools ? '92%' : '82%' }}>
            <div className={classes.bubbleBody} style={bubbleDynamicStyle}>
                {first && <span className={classes.bubbleTail} style={tailDynamicStyle} aria-hidden="true" />}
                {bubble.tools.map((t, i) => (
                    <ToolSection key={`tool-${i}`} tool={t} />
                ))}
                {hasText && (
                    <div className={hasTools ? classes.bubbleTextWrap : undefined}><TextNode content={bubble.text as AIContent} altHint={bubble.imageAlt} /></div>
                )}
            </div>
        </div>
    );
};

const MessageGroup = ({ group, model }: { group: Extract<GroupVM, { kind: 'group' }>; model?: string }) => {
    const classes = useStyles();
    const me = group.role === 'user';
    const name = me ? 'You' : 'Assistant';
    const meta = me ? undefined : model;

    return (
        <div className={classes.groupGrid} style={{ gridTemplateColumns: me ? '1fr 32px' : '32px 1fr' }}>
            <div
                className={classes.avatarBase}
                style={{
                    gridColumn: me ? 2 : 1,
                    background: me ? 'var(--brand-background)' : 'linear-gradient(135deg, var(--palette-berry-foreground), var(--brand-background))',
                }}
                aria-hidden="true"
            >
                {me ? <PersonIcon /> : <SparkleIcon />}
            </div>
            <div className={classes.groupCol} style={{ gridColumn: me ? 1 : 2, alignItems: me ? 'flex-end' : 'flex-start' }}>
                <div className={classes.groupHead} style={me ? { flexDirection: 'row-reverse' } : undefined}>
                    <span className={classes.groupName}>{name}</span>
                    {meta && <span className={classes.groupTime}>{meta}</span>}
                </div>
                {group.bubbles.map((b, i) => (
                    <Bubble key={`b-${i}`} bubble={b} me={me} first={i === 0} />
                ))}
            </div>
        </div>
    );
};

const SystemGroup = ({ content, imageAlt }: { content: AIContent; imageAlt?: string }) => {
    const classes = useStyles();
    return (
        <div className={classes.systemWrap}>
            <div className={classes.systemBubble}><TextNode content={content} altHint={imageAlt} /></div>
        </div>
    );
};

export const TranscriptBlock = ({ messages, model: modelProp }: { messages: ChatMessageDisplay[]; model?: string }) => {
    const { dataset, prettifyJson } = useReportContext();
    const classes = useStyles();
    const s = useReportStyles();
    const headingId = useId();
    const groups = buildGroups(messages, prettifyJson);
    const date = reportDate(dataset.createdAt);

    return (
        <Card appearance="outline" className="eval-transcript">
            <div className={classes.card}>
                <div className={classes.headerRow}>
                    <h2 id={headingId} className={s.eyebrow} style={{ margin: 0 }}>Transcript</h2>
                </div>
                <div className={classes.blockBody} role="region" aria-labelledby={headingId}>
                    {groups.length === 0 && <div className={classes.empty}>No transcript for this case.</div>}
                    {groups.length > 0 && date && (
                        <div className={classes.divider}>
                            <span className={classes.dividerLine} />
                            <span className={classes.dividerLabel}>{date}</span>
                            <span className={classes.dividerLine} />
                        </div>
                    )}
                    {groups.map((group, index) =>
                        group.kind === 'system' ? (
                            <SystemGroup key={`grp-${index}`} content={group.content} imageAlt={group.imageAlt} />
                        ) : (
                            <MessageGroup key={`grp-${index}`} group={group} model={modelProp} />
                        ),
                    )}
                </div>
            </div>
        </Card>
    );
};
