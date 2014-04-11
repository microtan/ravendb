﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using Voron.Debugging;
using Voron.Impl;
using Voron.Impl.FileHeaders;
using Voron.Impl.Paging;

namespace Voron.Trees
{
	using Exceptions;

	public unsafe class Tree
	{
		private TreeMutableState _state = new TreeMutableState();

		public string Name { get; set; }

		public TreeMutableState State
		{
			get { return _state; }
		}

		private readonly SliceComparer _cmp;

		private Tree(SliceComparer cmp, long root)
		{
			_cmp = cmp;
			_state.RootPageNumber = root;
		}

		private Tree(SliceComparer cmp, TreeMutableState state)
		{
			_cmp = cmp;
			_state = state;
		}

		public static Tree Open(Transaction tx, SliceComparer cmp, TreeRootHeader* header)
		{
			return new Tree(cmp, header->RootPageNumber)
			{
				_state =
				{
					PageCount = header->PageCount,
					BranchPages = header->BranchPages,
					Depth = header->Depth,
					OverflowPages = header->OverflowPages,
					LeafPages = header->LeafPages,
					EntriesCount = header->EntriesCount,
					Flags = header->Flags,
                    InWriteTransaction = tx.Flags.HasFlag(TransactionFlags.ReadWrite)
				}
			};
		}

		public static Tree Create(Transaction tx, SliceComparer cmp, TreeFlags flags = TreeFlags.None)
		{
			var newRootPage = NewPage(tx, PageFlags.Leaf, 1);
			var tree = new Tree(cmp, newRootPage.PageNumber)
			{
				_state =
				{
					Depth = 1,
					Flags = flags,
                    InWriteTransaction = true
				}
			};
			
			tree.State.RecordNewPage(newRootPage, 1);
			return tree;
		}

		public void Add(Transaction tx, Slice key, Stream value, ushort? version = null)
		{
		    if (value == null) throw new ArgumentNullException("value");
		    if (value.Length > int.MaxValue)
		        throw new ArgumentException("Cannot add a value that is over 2GB in size", "value");

			State.IsModified = true;
			var pos = DirectAdd(tx, key, (int)value.Length, version: version);
		    
		    CopyStreamToPointer(tx, value, pos);
		}


		public void Add(Transaction tx, Slice key, byte[] value, ushort? version = null)
		{
			if (value == null) throw new ArgumentNullException("value");

			State.IsModified = true;
			var pos = DirectAdd(tx, key, (int)value.Length, version: version);

			fixed (byte* src = value)
			{
				NativeMethods.memcpy(pos, src, value.Length);
			}
		}

		public void Add(Transaction tx, Slice key, Slice value, ushort? version = null)
		{
			if (value == null) throw new ArgumentNullException("value");

			State.IsModified = true;
			var pos = DirectAdd(tx, key, value.Size, version: version);

			value.CopyTo(pos);
		}

		private static void CopyStreamToPointer(Transaction tx, Stream value, byte* pos)
		{
			var temporaryPage = tx.Environment.TemporaryPage;
			var tempPageBuffer = temporaryPage.TempPageBuffer;
			var tempPagePointer = temporaryPage.TempPagePointer;
			while (true)
			{
				var read = value.Read(tempPageBuffer, 0, AbstractPager.PageSize);
				if (read == 0)
					break;
				NativeMethods.memcpy(pos, tempPagePointer, read);
				pos += read;
			}
		}

		public void MultiDelete(Transaction tx, Slice key, Slice value, ushort? version = null)
	    {
		    State.IsModified = true;
			Lazy<Cursor> lazy;
		    var page = FindPageFor(tx, key, out lazy);
		    if (page == null || page.LastMatch != 0)
		    {
			    return; //nothing to delete - key not found
		    }

			page = tx.ModifyPage(page.PageNumber, page);

		    var item = page.GetNode(page.LastSearchPosition);

		    if (item->Flags == NodeFlags.MultiValuePageRef) //multi-value tree exists
		    {
			    var tree = OpenOrCreateMultiValueTree(tx, key, item);

			    tree.Delete(tx, value, version);

			    if (tree.State.EntriesCount > 1)
				    return;

			    if (tree.State.EntriesCount == 0)
			    {
					tx.TryRemoveMultiValueTree(this, key);
					tx.FreePage(tree.State.RootPageNumber);
				    return;
			    }

			    // convert back to simple key/val
			    var iterator = tree.Iterate(tx);
			    if (!iterator.Seek(Slice.BeforeAllKeys))
				    throw new InvalidDataException(
					    "MultiDelete() failed : sub-tree is empty where it should not be, this is probably a Voron bug.");

			    var dataToSave = iterator.CurrentKey;

			    if (iterator.Current->DataSize != 0)
				    return; // we can't move this to a key/value, it has a stream value

			    var ptr = DirectAdd(tx, key, dataToSave.Size);
			    dataToSave.CopyTo(ptr);

			    tx.TryRemoveMultiValueTree(this, key);
			    tx.FreePage(tree.State.RootPageNumber);
		    }
		    else //the regular key->value pattern
		    {
			    Delete(tx, key, version);
		    }
	    }

		public void MultiAdd(Transaction tx, Slice key, Slice value, Stream stream = null, ushort? version = null)
		{
			if (value == null) throw new ArgumentNullException("value");
			if (value.Size > tx.DataPager.MaxNodeSize)
				throw new ArgumentException(
					"Cannot add a value to child tree that is over " + tx.DataPager.MaxNodeSize + " bytes in size", "value");
			if (value.Size == 0)
				throw new ArgumentException("Cannot add empty value to child tree");

			State.IsModified = true;

			Lazy<Cursor> lazy;
			var page = FindPageFor(tx, key, out lazy);
			if ((page == null || page.LastMatch != 0)) 
			{
				if (stream != null)
				{
					// can only use this optimizastion if we don't have a stream to store, so have to 
					// create a tree here anyway
					var tree = Create(tx, _cmp, TreeFlags.MultiValue);
					var pos = tree.DirectAdd(tx, value,  (int)stream.Length);
					CopyStreamToPointer(tx, stream, pos);
					tx.AddMultiValueTree(this, key, tree);

					// we need to record that we switched to tree mode here, so the next call wouldn't also try to create the tree again
					DirectAdd(tx, key, sizeof(TreeRootHeader), NodeFlags.MultiValuePageRef);
					return;
				}
				var ptr = DirectAdd(tx, key, value.Size, version: version);
				value.CopyTo(ptr);
				return;
			}

			page = tx.ModifyPage(page.PageNumber, page);

			var item = page.GetNode(page.LastSearchPosition);

			CheckConcurrency(key, version, item->Version, TreeActionType.Add);
			var existingValue = new Slice(DirectRead(tx, key), (ushort) item->DataSize);
			if (existingValue.Compare(value, _cmp) == 0)
				return; //nothing to do, the exact value is already there				


			if (item->Flags == NodeFlags.MultiValuePageRef)
			{
				var tree = OpenOrCreateMultiValueTree(tx, key, item);
				var pos = tree.DirectAdd(tx, value, stream == null ? 0 : (int)stream.Length);
				if(stream != null)
					CopyStreamToPointer(tx, stream, pos);
			}
			else // need to turn to tree
			{
				var tree = Create(tx, _cmp, TreeFlags.MultiValue);
				var current = NodeHeader.GetData(tx, item);
				tree.DirectAdd(tx, current, 0);
				tree.DirectAdd(tx, value, 0);
				tx.AddMultiValueTree(this, key, tree);

				// we need to record that we switched to tree mode here, so the next call wouldn't also try to create the tree again
				DirectAdd(tx, key, sizeof (TreeRootHeader), NodeFlags.MultiValuePageRef);
			}
		}

		internal byte* DirectAdd(Transaction tx, Slice key, int len, NodeFlags nodeType = NodeFlags.Data, ushort? version = null)
		{
			Debug.Assert(nodeType == NodeFlags.Data || nodeType == NodeFlags.MultiValuePageRef);

			if (tx.Flags == (TransactionFlags.ReadWrite) == false)
				throw new ArgumentException("Cannot add a value in a read only transaction");

			if (key.Size > tx.DataPager.MaxNodeSize)
				throw new ArgumentException(
					"Key size is too big, must be at most " + tx.DataPager.MaxNodeSize + " bytes, but was " + key.Size, "key");

			Lazy<Cursor> lazy;
			var foundPage = FindPageFor(tx, key, out lazy);

			var page = tx.ModifyPage(foundPage.PageNumber, foundPage);

			ushort nodeVersion = 0;
			bool? shouldGoToOverflowPage = null;
			if (page.LastMatch == 0) // this is an update operation
			{
				var node = page.GetNode(page.LastSearchPosition);

				Debug.Assert(node->KeySize == key.Size && new Slice(node).Equals(key));

				shouldGoToOverflowPage = tx.DataPager.ShouldGoToOverflowPage(len);

				byte* pos;
				if (shouldGoToOverflowPage == false)
				{
					// optimization for Data and MultiValuePageRef - try to overwrite existing node space
					if (TryOverwriteDataOrMultiValuePageRefNode(node, key, len, nodeType, version, out pos))
						return pos;
				}
				else
				{
					// optimization for PageRef - try to overwrite existing overflows
					if (TryOverwriteOverflowPages(tx, State, node, key, len, version, out pos))
						return pos;
				}

				RemoveLeafNode(tx, page, out nodeVersion);
			}
			else // new item should be recorded
			{
				State.EntriesCount++;
			}

			CheckConcurrency(key, version, nodeVersion, TreeActionType.Add);

			var lastSearchPosition = page.LastSearchPosition; // searching for overflow pages might change this
			byte* overFlowPos = null;
			var pageNumber = -1L;
			if (shouldGoToOverflowPage ?? tx.DataPager.ShouldGoToOverflowPage(len))
			{
				pageNumber = WriteToOverflowPages(tx, State, len, out overFlowPos);
				len = -1;
				nodeType = NodeFlags.PageRef;
			}

			byte* dataPos;
			if (page.HasSpaceFor(tx, key, len) == false)
			{
			    var cursor = lazy.Value;
			    cursor.Update(cursor.Pages.First, page);
			    
			    var pageSplitter = new PageSplitter(tx, this, _cmp, key, len, pageNumber, nodeType, nodeVersion, cursor, State);
			    dataPos = pageSplitter.Execute();

			    DebugValidateTree(tx, State.RootPageNumber);
			}
			else
			{
				switch (nodeType)
				{
					case NodeFlags.PageRef:
						dataPos = page.AddPageRefNode(lastSearchPosition, key, pageNumber);
						break;
					case NodeFlags.Data:
						dataPos = page.AddDataNode(lastSearchPosition, key, len, nodeVersion);
						break;
					case NodeFlags.MultiValuePageRef:
						dataPos = page.AddMultiValueNode(lastSearchPosition, key, len, nodeVersion);
						break;
					default:
						throw new NotSupportedException("Unknown node type for direct add operation: " + nodeType);
				}
				page.DebugValidate(tx, _cmp, State.RootPageNumber);
			}
			if (overFlowPos != null)
				return overFlowPos;
			return dataPos;
		}

		public bool SetAsMultiValueTreeRef(Transaction tx, Slice key)
		{
			Lazy<Cursor> lazy;
			var foundPage = FindPageFor(tx, key, out lazy);
			var page = tx.ModifyPage(foundPage.PageNumber, foundPage);

			if (page.LastMatch != 0)
				return false; // not there

			var nodeHeader = page.GetNode(page.LastSearchPosition);
			if (nodeHeader->Flags == NodeFlags.MultiValuePageRef)
				return false;
			if (nodeHeader->Flags != NodeFlags.Data)
				throw new InvalidOperationException("Only data nodes can be set to MultiValuePageRef");
			nodeHeader->Flags = NodeFlags.MultiValuePageRef;
			return true;
		}

		private long WriteToOverflowPages(Transaction tx, TreeMutableState txInfo, int overflowSize, out byte* dataPos)
		{
			var numberOfPages = tx.DataPager.GetNumberOfOverflowPages(overflowSize);
			var overflowPageStart = tx.AllocatePage(numberOfPages);
			overflowPageStart.Flags = PageFlags.Overflow;
			overflowPageStart.OverflowSize = overflowSize;
			dataPos = overflowPageStart.Base + Constants.PageHeaderSize;
			txInfo.OverflowPages += numberOfPages;
			txInfo.PageCount += numberOfPages;
			return overflowPageStart.PageNumber;
		}

		private void RemoveLeafNode(Transaction tx, Page page, out ushort nodeVersion)
		{
			var node = page.GetNode(page.LastSearchPosition);
			nodeVersion = node->Version;
			if (node->Flags == (NodeFlags.PageRef)) // this is an overflow pointer
			{
				var overflowPage = tx.GetReadOnlyPage(node->PageNumber);
				var numberOfPages = tx.DataPager.GetNumberOfOverflowPages(overflowPage.OverflowSize);
				for (int i = 0; i < numberOfPages; i++)
				{
					tx.FreePage(overflowPage.PageNumber + i);
				}

				State.OverflowPages -= numberOfPages;
				State.PageCount -= numberOfPages;
			}
			page.RemoveNode(page.LastSearchPosition);
		}

		[Conditional("VALIDATE")]
		public void DebugValidateTree(Transaction tx, long rootPageNumber)
		{
			var pages = new HashSet<long>();
			var stack = new Stack<Page>();
			var root = tx.GetReadOnlyPage(rootPageNumber);
			stack.Push(root);
			pages.Add(rootPageNumber);
			while (stack.Count > 0)
			{
				var p = stack.Pop();
				if (p.NumberOfEntries == 0 && p != root)
				{
					DebugStuff.RenderAndShow(tx, rootPageNumber, 1);
					throw new InvalidOperationException("The page " + p.PageNumber + " is empty");

				}
				p.DebugValidate(tx, _cmp, rootPageNumber);
				if (p.IsBranch == false)
					continue;
				for (int i = 0; i < p.NumberOfEntries; i++)
				{
					var page = p.GetNode(i)->PageNumber;
					if (pages.Add(page) == false)
					{
						DebugStuff.RenderAndShow(tx, rootPageNumber, 1);
						throw new InvalidOperationException("The page " + page + " already appeared in the tree!");
					}
					stack.Push(tx.GetReadOnlyPage(page));
				}
			}
		}

		public Page FindPageFor(Transaction tx, Slice key, out Lazy<Cursor> cursor)
		{
			Page p;

		    if (TryUseRecentTransactionPage(tx, key, out cursor, out p))
		    {
		        return p;
		    }

			return SearchForPage(tx, key, ref cursor);
		}

	    private Page SearchForPage(Transaction tx, Slice key, ref Lazy<Cursor> cursor)
	    {
	        var p = tx.GetReadOnlyPage(State.RootPageNumber);
	        var c = new Cursor();
	        c.Push(p);

	        bool rightmostPage = true;
	        bool leftmostPage = true;

	        while (p.Flags == (PageFlags.Branch))
	        {
	            int nodePos;
	            if (key.Options == SliceOptions.BeforeAllKeys)
	            {
	                p.LastSearchPosition = nodePos = 0;
	                rightmostPage = false;
	            }
	            else if (key.Options == SliceOptions.AfterAllKeys)
	            {
	                p.LastSearchPosition = nodePos = (ushort) (p.NumberOfEntries - 1);
	                leftmostPage = false;
	            }
	            else
	            {
	                if (p.Search(key, _cmp) != null)
	                {
	                    nodePos = p.LastSearchPosition;
	                    if (p.LastMatch != 0)
	                    {
	                        nodePos--;
	                        p.LastSearchPosition--;
	                    }

	                    if (nodePos != 0)
	                        leftmostPage = false;

	                    rightmostPage = false;
	                }
	                else
	                {
	                    nodePos = (ushort) (p.LastSearchPosition - 1);

	                    leftmostPage = false;
	                }
	            }

	            var node = p.GetNode(nodePos);
	            p = tx.GetReadOnlyPage(node->PageNumber);
	            Debug.Assert(node->PageNumber == p.PageNumber,
	                string.Format("Requested Page: #{0}. Got Page: #{1}", node->PageNumber, p.PageNumber));

	            c.Push(p);
	        }

	        if (p.IsLeaf == false)
	            throw new DataException("Index points to a non leaf page");

	        p.Search(key, _cmp); // will set the LastSearchPosition

	        AddToRecentlyFoundPages(tx, c, p, leftmostPage, rightmostPage);

	        cursor = new Lazy<Cursor>(() => c);
	        return p;
	    }

	    private void AddToRecentlyFoundPages(Transaction tx, Cursor c, Page p, bool? leftmostPage, bool? rightmostPage)
	    {
	        var foundPage = new RecentlyFoundPages.FoundPage(c.Pages.Count)
	        {
	            Number = p.PageNumber,
	            FirstKey = leftmostPage == true ? Slice.BeforeAllKeys : p.GetNodeKey(0),
	            LastKey = rightmostPage == true ? Slice.AfterAllKeys : p.GetNodeKey(p.NumberOfEntries - 1),
	        };
	        var cur = c.Pages.First;
	        int pos = foundPage.CursorPath.Length - 1;
	        while (cur != null)
	        {
	            foundPage.CursorPath[pos--] = cur.Value.PageNumber;
	            cur = cur.Next;
	        }

	        tx.AddRecentlyFoundPage(this, foundPage);
	    }

	    private bool TryUseRecentTransactionPage(Transaction tx, Slice key, out Lazy<Cursor> cursor, out Page page)
		{
			page = null;
			cursor = null;

			var recentPages = tx.GetRecentlyFoundPages(this);

			if (recentPages == null)
				return false;

			var foundPage = recentPages.Find(key);

			if (foundPage == null)
				return false;

			var lastFoundPageNumber = foundPage.Number;
			page = tx.GetReadOnlyPage(lastFoundPageNumber);

			if (page.IsLeaf == false)
				throw new DataException("Index points to a non leaf page");

			page.NodePositionFor(key, _cmp); // will set the LastSearchPosition

			var cursorPath = foundPage.CursorPath;
			var pageCopy = page;
			cursor = new Lazy<Cursor>(() =>
			{
				var c = new Cursor();
				foreach (var p in cursorPath)
				{
					if (p == lastFoundPageNumber)
						c.Push(pageCopy);
					else
					{
						var cursorPage = tx.GetReadOnlyPage(p);
						if (key.Options == SliceOptions.BeforeAllKeys)
						{
							cursorPage.LastSearchPosition = 0;
						}
						else if (key.Options == SliceOptions.AfterAllKeys)
						{
							cursorPage.LastSearchPosition = (ushort)(cursorPage.NumberOfEntries - 1);
						}
						else if (cursorPage.Search(key, _cmp) != null)
						{
							if (cursorPage.LastMatch != 0)
							{
								cursorPage.LastSearchPosition--;
							}
						}

						c.Push(cursorPage);
					}
				}

				return c;
			});

			return true;
		}

		internal static Page NewPage(Transaction tx, PageFlags flags, int num)
		{
			var page = tx.AllocatePage(num);
			page.Flags = flags;

			return page;
		}

		public void Delete(Transaction tx, Slice key, ushort? version = null)
		{
			if (tx.Flags == (TransactionFlags.ReadWrite) == false)
				throw new ArgumentException("Cannot delete a value in a read only transaction");

			State.IsModified = true;
			Lazy<Cursor> lazy;
			var page = FindPageFor(tx, key, out lazy);

			page.NodePositionFor(key, _cmp);
			if (page.LastMatch != 0)
				return; // not an exact match, can't delete

			page = tx.ModifyPage(page.PageNumber, page);

			State.EntriesCount--;
			ushort nodeVersion;
			RemoveLeafNode(tx, page, out nodeVersion);

			CheckConcurrency(key, version, nodeVersion, TreeActionType.Delete);

			var treeRebalancer = new TreeRebalancer(tx, this);
			var changedPage = page;
			while (changedPage != null)
			{
				changedPage = treeRebalancer.Execute(lazy.Value, changedPage);
			}

			page.DebugValidate(tx, _cmp, State.RootPageNumber);
		}

		public TreeIterator Iterate(Transaction tx, WriteBatch writeBatch = null)
		{
			return new TreeIterator(this, tx, _cmp);
		}

		public ReadResult Read(Transaction tx, Slice key)
		{
			Lazy<Cursor> lazy;
			var p = FindPageFor(tx, key, out lazy);

            if (p.LastMatch != 0)
		        return null;

		    var node = p.GetNode(p.LastSearchPosition);

		    return new ReadResult(NodeHeader.Reader(tx, node), node->Version);
		}

		public int GetDataSize(Transaction tx, Slice key)
		{
			Lazy<Cursor> lazy;
			var p = FindPageFor(tx, key, out lazy);
			var node = p.Search(key, _cmp);

			if (node == null || new Slice(node).Compare(key, _cmp) != 0)
				return -1;

			return node->DataSize;
		}

		public ushort ReadVersion(Transaction tx, Slice key)
		{
			Lazy<Cursor> lazy;
			var p = FindPageFor(tx, key, out lazy);
			var node = p.Search(key, _cmp);

			if (node == null || new Slice(node).Compare(key, _cmp) != 0)
				return 0;

			return node->Version;
		}

		public IIterator MultiRead(Transaction tx, Slice key)
		{
			Lazy<Cursor> lazy;
			var page = FindPageFor(tx, key, out lazy);

			if (page == null || page.LastMatch != 0)
			{
				return new EmptyIterator();
			}

			var item = page.Search(key, _cmp);

			var fetchedNodeKey = new Slice(item);
			if (fetchedNodeKey.Compare(key, _cmp) != 0)
			{
				throw new InvalidDataException("Was unable to retrieve the correct node. Data corruption possible");
			}

			if (item->Flags == NodeFlags.MultiValuePageRef)
			{
				var tree = OpenOrCreateMultiValueTree(tx, key, item);

				return tree.Iterate(tx);
			}

			return new SingleEntryIterator(_cmp, item, tx);
		}

		internal byte* DirectRead(Transaction tx, Slice key)
		{
			Lazy<Cursor> lazy;
			var p = FindPageFor(tx, key, out lazy);
			var node = p.Search(key, _cmp);

			if (node == null)
				return null;

			var item1 = new Slice(node);

			if (item1.Compare(key, _cmp) != 0)
				return null;

			if (node->Flags == (NodeFlags.PageRef))
			{
				var overFlowPage = tx.GetReadOnlyPage(node->PageNumber);
				return overFlowPage.Base + Constants.PageHeaderSize;
			}

			return (byte*) node + node->KeySize + Constants.NodeHeaderSize;
		}

		internal void SetState(TreeMutableState state)
		{
			_state = state;
		}

		public List<long> AllPages(Transaction tx)
		{
			var results = new List<long>();
			var stack = new Stack<Page>();
			var root = tx.GetReadOnlyPage(State.RootPageNumber);
			stack.Push(root);
			while (stack.Count > 0)
			{
				var p = stack.Pop();
				results.Add(p.PageNumber);
				for (int i = 0; i < p.NumberOfEntries; i++)
				{
					var node = p.GetNode(i);
					var pageNumber = node->PageNumber;
					if (p.IsBranch)
					{
						stack.Push(tx.GetReadOnlyPage(pageNumber));
					}
					else if (node->Flags == NodeFlags.PageRef)
					{
						// This is an overflow page
						var overflowPage = tx.GetReadOnlyPage(pageNumber);
						var numberOfPages = tx.DataPager.GetNumberOfOverflowPages(overflowPage.OverflowSize);
						for (long j = 0; j < numberOfPages; ++j)
							results.Add(overflowPage.PageNumber + j);
					}
					else if (node->Flags == NodeFlags.MultiValuePageRef)
					{
						var childTreeHeader = (TreeRootHeader*)((byte*)node + node->KeySize + Constants.NodeHeaderSize);

						results.Add(childTreeHeader->RootPageNumber);

						// this is a multi value
						var tree = OpenOrCreateMultiValueTree(tx, new Slice(node), node);
						results.AddRange(tree.AllPages(tx));
					}
				}
			}
			return results;
		}

		public override string ToString()
		{
			return Name + " " + State.EntriesCount;
		}

		internal Tree OpenOrCreateMultiValueTree(Transaction tx, Slice key, NodeHeader* item)
		{
			Tree tree;
			if (tx.TryGetMultiValueTree(this, key, out tree))
				return tree;

		    var childTreeHeader =
		        (TreeRootHeader*) ((byte*) item + item->KeySize + Constants.NodeHeaderSize);
			Debug.Assert(childTreeHeader->RootPageNumber < tx.State.NextPageNumber);
            tree = childTreeHeader != null ?
				Open(tx, _cmp, childTreeHeader) :
				Create(tx, _cmp);

			tx.AddMultiValueTree(this, key, tree);
			return tree;
		}

		private void CheckConcurrency(Slice key, ushort? expectedVersion, ushort nodeVersion, TreeActionType actionType)
		{
			if (expectedVersion.HasValue && nodeVersion != expectedVersion.Value)
				throw new ConcurrencyException(string.Format("Cannot {0} '{1}' to '{4}' tree. Version mismatch. Expected: {2}. Actual: {3}.", actionType.ToString().ToLowerInvariant(), key, expectedVersion.Value, nodeVersion, Name));
		}

		public bool IsMultiValueTree { get; set; }

		public enum TreeActionType
		{
			Add,
			Delete
		}

		public Tree Clone()
		{
			return new Tree(_cmp, _state.Clone()){ Name = Name };
		}

		private bool TryOverwriteDataOrMultiValuePageRefNode(NodeHeader* updatedNode, Slice key, int len,
															NodeFlags requestedNodeType, ushort? version,
															out byte* pos)
		{
			switch (requestedNodeType)
			{
				case NodeFlags.Data:
				case NodeFlags.MultiValuePageRef:
					{
						if (updatedNode->DataSize == len &&
							(updatedNode->Flags == NodeFlags.Data || updatedNode->Flags == NodeFlags.MultiValuePageRef))
						{
							CheckConcurrency(key, version, updatedNode->Version, TreeActionType.Add);

							if (updatedNode->Version == ushort.MaxValue)
								updatedNode->Version = 0;
							updatedNode->Version++;

							updatedNode->Flags = requestedNodeType;

							{
								pos = (byte*)updatedNode + Constants.NodeHeaderSize + key.Size;
								return true;
							}
						}
						break;
					}
				case NodeFlags.PageRef:
					throw new InvalidOperationException("We never add PageRef explicitly");
				default:
					throw new ArgumentOutOfRangeException();
			}
			pos = null;
			return false;
		}

		private bool TryOverwriteOverflowPages(Transaction tx, TreeMutableState treeState, NodeHeader* updatedNode,
													  Slice key, int len, ushort? version, out byte* pos)
		{
			if (updatedNode->Flags == NodeFlags.PageRef &&
				tx.Id <= tx.Environment.OldestTransaction) // ensure MVCC - do not overwrite if there is some older active transaction that might read those overflows
			{
				var overflowPage = tx.GetReadOnlyPage(updatedNode->PageNumber);

				if (len <= overflowPage.OverflowSize)
				{
					CheckConcurrency(key, version, updatedNode->Version, TreeActionType.Add);

					if (updatedNode->Version == ushort.MaxValue)
						updatedNode->Version = 0;
					updatedNode->Version++;

					var availableOverflows = tx.DataPager.GetNumberOfOverflowPages(overflowPage.OverflowSize);

					var requestedOverflows = tx.DataPager.GetNumberOfOverflowPages(len);

					var overflowsToFree = availableOverflows - requestedOverflows;

					for (int i = 0; i < overflowsToFree; i++)
					{
						tx.FreePage(overflowPage.PageNumber + requestedOverflows + i);
					}

					treeState.OverflowPages -= overflowsToFree;
					treeState.PageCount -= overflowsToFree;

					overflowPage.OverflowSize = len;

					pos = overflowPage.Base + Constants.PageHeaderSize;
					return true;
				}
			}
			pos = null;
			return false;
		}
	}
}